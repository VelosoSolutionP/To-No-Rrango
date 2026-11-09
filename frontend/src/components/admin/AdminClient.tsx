"use client";

import Link from "next/link";
import { Fragment, useCallback, useEffect, useMemo, useState } from "react";
import { ApiError } from "@/lib/api";
import { UNAUTHORIZED_EVENT, adminApi, createAdminHttp, loadModules, login } from "@/lib/admin-api";
import { clearAdminSession, loadAdminSession, saveAdminSession } from "@/lib/admin-session";
import type {
  AdminOrder,
  AdminSession,
  CategoryPayload,
  CreateEstablishmentPayload,
  Establishment,
  MenuCategory,
  MenuItem,
  MenuItemPayload,
  OrderStatus,
  PublicMenu,
  UpdateEstablishmentPayload,
  UserRole,
} from "@/lib/types";
import { AdminEstablishment } from "./AdminEstablishment";
import { AdminLogin } from "./AdminLogin";
import { AdminMenu } from "./AdminMenu";
import { AdminOrders, type CourierContact } from "./AdminOrders";
import { AdminContext } from "./AdminContext";
import "./admin.css";
import { Distribution } from "./modules/Distribution";
import { Finance } from "./modules/Finance";
import { Fiscal } from "./modules/Fiscal";
import { Inventory } from "./modules/Inventory";
import { Kitchen } from "./modules/Kitchen";
import { LiveDeliveries } from "./modules/LiveDeliveries";
import { Overview } from "./modules/Overview";
import { Customers } from "./modules/Parties";
import { Settings } from "./modules/Settings";
import { Zest } from "./modules/Zest";

type Tab = "overview" | "orders" | "tracking" | "kitchen" | "menu" | "inventory" | "customers" | "distribution" | "finance" | "fiscal" | "whatsapp" | "settings";

const roleName: Record<UserRole, string> = { Owner: "Dono", Manager: "Gerente", Attendant: "Atendente", Kitchen: "Cozinha" };

const management: UserRole[] = ["Owner", "Manager"];
const everyone: UserRole[] = ["Owner", "Manager", "Attendant", "Kitchen"];

const tabs: Array<{ id: Tab; group: string; label: string; roles: UserRole[]; modules?: string[] }> = [
  { id: "overview", group: "Operação", label: "Visão geral", roles: ["Owner", "Manager", "Attendant"] },
  { id: "orders", group: "Operação", label: "Pedidos", roles: everyone, modules: ["pedidos"] },
  { id: "tracking", group: "Operação", label: "Rastreio", roles: ["Owner", "Manager", "Attendant"], modules: ["rastreio"] },
  { id: "kitchen", group: "Operação", label: "Cozinha", roles: everyone, modules: ["pedidos"] },
  { id: "menu", group: "Gestão", label: "Cardápio", roles: management, modules: ["cardapio"] },
  { id: "inventory", group: "Gestão", label: "Estoque", roles: management, modules: ["estoque"] },
  { id: "customers", group: "Gestão", label: "Clientes", roles: ["Owner", "Manager", "Attendant"], modules: ["clientes"] },
  { id: "distribution", group: "Gestão", label: "Distribuição", roles: management, modules: ["pedidos"] },
  { id: "finance", group: "Gestão", label: "Financeiro", roles: management, modules: ["financeiro"] },
  { id: "fiscal", group: "Gestão", label: "Fiscal", roles: management, modules: ["fiscal", "fiscal_nfce"] },
  { id: "whatsapp", group: "Inteligência", label: "WhatsApp e IA", roles: ["Owner", "Manager", "Attendant"], modules: ["ia", "whatsapp"] },
  { id: "settings", group: "Ajustes", label: "Configurações", roles: management },
];

function messageOf(error: unknown): string {
  if (error instanceof ApiError) {
    return error.detail ?? error.message;
  }

  return error instanceof Error ? error.message : "Ocorreu um erro inesperado.";
}

export function AdminClient() {
  const [session, setSession] = useState<AdminSession | null>(null);
  const [booting, setBooting] = useState(true);
  const [authBusy, setAuthBusy] = useState(false);
  const [authError, setAuthError] = useState<string | null>(null);
  const [establishments, setEstablishments] = useState<Establishment[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [tab, setTab] = useState<Tab>("overview");
  const [menu, setMenu] = useState<PublicMenu | null>(null);
  const [orders, setOrders] = useState<AdminOrder[]>([]);
  const [dataLoading, setDataLoading] = useState(false);
  const [dataError, setDataError] = useState<string | null>(null);
  const [mutationBusy, setMutationBusy] = useState(false);
  const [mutationError, setMutationError] = useState<string | null>(null);
  const [navOpen, setNavOpen] = useState(false);

  const selectedEstablishment = useMemo(
    () => establishments.find((establishment) => establishment.id === selectedId) ?? null,
    [establishments, selectedId],
  );

  const http = useMemo(() => (session ? createAdminHttp(session) : null), [session?.token]); // eslint-disable-line react-hooks/exhaustive-deps

  const visibleTabs = useMemo(
    () =>
      session
        ? tabs.filter(
            (item) =>
              item.roles.includes(session.role) &&
              (!item.modules || item.modules.some((module) => session.modules.includes(module))),
          )
        : [],
    [session],
  );

  const loadWorkspace = useCallback(async (activeSession: AdminSession, preferredId?: string) => {
    setDataLoading(true);
    setDataError(null);

    try {
      const list = await adminApi.listEstablishments(activeSession);
      setEstablishments(list);

      if (list.length === 0) {
        setSelectedId("");
        setMenu(null);
        setOrders([]);
        setTab("settings");
        const next = { ...activeSession, establishmentId: undefined };
        setSession(next);
        saveAdminSession(next);
        return;
      }

      const nextId =
        [preferredId, activeSession.establishmentId].find(
          (id) => id && list.some((establishment) => establishment.id === id),
        ) ?? list[0].id;

      const next = { ...activeSession, establishmentId: nextId };
      setSelectedId(nextId);
      setSession(next);
      saveAdminSession(next);

      const [menuData, ordersData] = await Promise.all([
        adminApi.getMenu(next, nextId),
        adminApi.listOrders(next, nextId),
      ]);

      setMenu(menuData);
      setOrders(ordersData);
    } catch (error) {
      setDataError(messageOf(error));
    } finally {
      setDataLoading(false);
    }
  }, []);

  useEffect(() => {
    const stored = loadAdminSession();

    if (!stored) {
      setBooting(false);
      return;
    }

    setSession(stored);
    void loadModules(stored)
      .then((modules) => {
        const refreshed = { ...stored, modules };
        saveAdminSession(refreshed);
        return loadWorkspace(refreshed);
      })
      .catch(() => loadWorkspace(stored))
      .finally(() => setBooting(false));
  }, [loadWorkspace]);

  useEffect(() => {
    const expire = () => {
      clearAdminSession();
      setSession(null);
    };

    window.addEventListener(UNAUTHORIZED_EVENT, expire);
    return () => window.removeEventListener(UNAUTHORIZED_EVENT, expire);
  }, []);

  useEffect(() => {
    if (session && visibleTabs.length > 0 && !visibleTabs.some((item) => item.id === tab)) {
      setTab(visibleTabs[0].id);
    }
  }, [session, tab, visibleTabs]);

  const refreshMenu = useCallback(async () => {
    if (!session || !selectedId) {
      return;
    }

    try {
      const menuData = await adminApi.getMenu(session, selectedId);
      setMenu(menuData);
      setDataError(null);
    } catch (error) {
      setDataError(messageOf(error));
    }
  }, [selectedId, session]);

  const refreshOrders = useCallback(async () => {
    if (!session || !selectedId) {
      return;
    }

    try {
      const ordersData = await adminApi.listOrders(session, selectedId);
      setOrders(ordersData);
      setDataError(null);
    } catch (error) {
      setDataError(messageOf(error));
    }
  }, [selectedId, session]);

  useEffect(() => {
    if (!session || !selectedId || (tab !== "orders" && tab !== "kitchen" && tab !== "overview")) {
      return;
    }

    const interval = window.setInterval(() => {
      void refreshOrders();
    }, 5000);

    return () => window.clearInterval(interval);
  }, [refreshOrders, selectedId, session, tab]);

  async function handleLogin(email: string, password: string) {
    setAuthBusy(true);
    setAuthError(null);

    try {
      const next = await login(email, password);
      saveAdminSession(next);
      setSession(next);
      setTab(next.role === "Kitchen" ? "kitchen" : "overview");
      await loadWorkspace(next);
    } catch (error) {
      setAuthError(messageOf(error));
    } finally {
      setAuthBusy(false);
    }
  }

  function handleLogout() {
    clearAdminSession();
    setSession(null);
    setEstablishments([]);
    setSelectedId("");
    setMenu(null);
    setOrders([]);
    setAuthError(null);
    setDataError(null);
    setMutationError(null);
    setTab("overview");
  }

  async function handleEstablishmentChange(establishmentId: string) {
    if (!session) {
      return;
    }

    await loadWorkspace(session, establishmentId);
  }

  async function runMutation(action: () => Promise<void>) {
    setMutationBusy(true);
    setMutationError(null);

    try {
      await action();
    } catch (error) {
      setMutationError(messageOf(error));
      throw error;
    } finally {
      setMutationBusy(false);
    }
  }

  async function saveCategory(categoryId: string | null, payload: CategoryPayload) {
    if (!session || !selectedId) {
      return;
    }

    await runMutation(async () => {
      if (categoryId) {
        await adminApi.updateCategory(session, categoryId, payload);
      } else {
        await adminApi.createCategory(session, selectedId, payload);
      }

      await refreshMenu();
    });
  }

  async function setCategoryActive(category: MenuCategory, isActive: boolean) {
    if (!session) {
      return;
    }

    await runMutation(async () => {
      await adminApi.setCategoryActive(session, category.id, isActive);
      await refreshMenu();
    });
  }

  async function saveItem(itemId: string | null, payload: MenuItemPayload) {
    if (!session || !selectedId) {
      return;
    }

    await runMutation(async () => {
      if (itemId) {
        await adminApi.updateItem(session, itemId, payload);
      } else {
        await adminApi.createItem(session, selectedId, payload);
      }

      await refreshMenu();
    });
  }

  async function setItemActive(item: MenuItem, isActive: boolean) {
    if (!session) {
      return;
    }

    await runMutation(async () => {
      await adminApi.setItemActive(session, item.id, isActive);
      await refreshMenu();
    });
  }

  async function setItemAvailability(item: MenuItem, isAvailable: boolean) {
    if (!session) {
      return;
    }

    await runMutation(async () => {
      await adminApi.setItemAvailability(session, item.id, isAvailable);
      await refreshMenu();
    });
  }

  async function updateOrderStatus(orderId: string, status: OrderStatus, reason: string | null) {
    if (!session) {
      return;
    }

    await runMutation(async () => {
      await adminApi.updateOrderStatus(session, orderId, {
        status,
        reason,
        changedBy: "painel",
      });
      await refreshOrders();
    });
  }

  async function markPaid(orderId: string) {
    if (!http) {
      return;
    }

    await runMutation(async () => {
      await http.post(`/orders/${orderId}/paid`);
      await refreshOrders();
    });
  }

  async function refundOrder(orderId: string) {
    if (!http) {
      return;
    }

    await runMutation(async () => {
      await http.post(`/orders/${orderId}/refund`);
      await refreshOrders();
    });
  }

  async function updateEstablishment(payload: UpdateEstablishmentPayload) {
    if (!session || !selectedId) {
      return;
    }

    await runMutation(async () => {
      await adminApi.updateEstablishment(session, selectedId, payload);
      await loadWorkspace(session, selectedId);
    });
  }

  async function createEstablishment(payload: CreateEstablishmentPayload) {
    if (!session) {
      return;
    }

    await runMutation(async () => {
      const created = await adminApi.createEstablishment(session, payload);
      await loadWorkspace(session, created.id);
      setTab("menu");
    });
  }

  if (booting) {
    return (
      <main className="vx-login">
        <p className="muted">Carregando painel...</p>
      </main>
    );
  }

  if (!session) {
    return <AdminLogin busy={authBusy} error={authError} onLogin={handleLogin} />;
  }

  const hasEstablishment = Boolean(selectedEstablishment);
  const currentTab = visibleTabs.find((item) => item.id === tab);
  const pendingOrders = orders.filter((order) => order.status === "Pending").length;
  const canManage = management.includes(session.role);
  const openOrders = orders.filter((order) => ["Pending", "Confirmed", "Preparing", "Ready", "OutForDelivery"].includes(order.status)).length;

  function goTo(next: Tab) {
    setTab(next);
    setNavOpen(false);
  }

  return (
    <AdminContext.Provider value={{ http, establishmentId: selectedId }}>
      <div className={`vx ${navOpen ? "is-open" : ""}`}>
        <aside className="vx-side">
          <Link className="vx-brand" href="/">
            <span className="vx-mark">TR</span>
            <div>
              <strong>To-No-Rango</strong>
              <small>Veloso · ZEST</small>
            </div>
          </Link>
          <nav aria-label="Áreas do painel" className="vx-nav">
            {visibleTabs.map((item, index) => (
              <Fragment key={item.id}>
                {item.group !== visibleTabs[index - 1]?.group ? <p className="vx-sec">{item.group}</p> : null}
                <button
                  aria-current={tab === item.id ? "page" : undefined}
                  className={tab === item.id ? "is-active" : ""}
                  disabled={!hasEstablishment && item.id !== "settings"}
                  onClick={() => goTo(item.id)}
                  type="button"
                >
                  <span>{item.label}</span>
                  {item.id === "orders" && pendingOrders > 0 ? <b>{pendingOrders}</b> : null}
                </button>
              </Fragment>
            ))}
          </nav>
          <div className="vx-foot">
            <span className="vx-avatar">{session.userName.slice(0, 1).toUpperCase()}</span>
            <span className="vx-user"><strong>{session.userName}</strong><small>{roleName[session.role]}</small></span>
            <button className="link-button" onClick={handleLogout} type="button">Sair</button>
          </div>
        </aside>
        <button aria-label="Fechar menu" className="vx-backdrop" onClick={() => setNavOpen(false)} type="button" />

        <div className="vx-body">
          <header className="vx-top">
            <button aria-label="Abrir menu" className="vx-menu-button" onClick={() => setNavOpen(true)} type="button">☰</button>
            <h1>{currentTab?.label ?? "Painel"}</h1>
            <div className="vx-top-right">
              {establishments.length > 1 ? (
                <select aria-label="Estabelecimento" className="admin-establishment-select" onChange={(event) => handleEstablishmentChange(event.target.value)} value={selectedId}>
                  {establishments.map((establishment) => <option key={establishment.id} value={establishment.id}>{establishment.name}</option>)}
                </select>
              ) : selectedEstablishment ? <span className="vx-top-store">{selectedEstablishment.name}</span> : null}
              {openOrders > 0 ? <span className="vx-pill"><i />{openOrders} em andamento</span> : null}
              {selectedEstablishment ? <a className="vx-top-link" href={`/menu/${selectedEstablishment.slug}`} rel="noreferrer" target="_blank">Ver cardápio</a> : null}
            </div>
          </header>

          <main className="vx-main">
            {dataError && tab !== "orders" && tab !== "kitchen" && tab !== "menu" ? <p className="form-error">{dataError}</p> : null}

            {tab === "overview" && selectedEstablishment && http ? (
              <Overview establishment={selectedEstablishment} http={http} modules={session.modules} onNavigate={goTo} orders={orders} />
            ) : null}

            {tab === "orders" && hasEstablishment ? (
              <AdminOrders
                error={mutationError ?? dataError}
                loading={dataLoading}
                onMarkPaid={session.role !== "Kitchen" ? markPaid : undefined}
                onRefresh={refreshOrders}
                onRefund={canManage && session.modules.includes("pagamentos") ? refundOrder : undefined}
                onTrack={http && session.role !== "Kitchen" && session.modules.includes("rastreio") ? async (orderId) => (await http.post<{ token: string }>(`/orders/${orderId}/tracking`)).token : undefined}
                onLoadCouriers={http && session.role !== "Kitchen" && session.modules.includes("rastreio") ? () => http.get<CourierContact[]>(`/establishments/${selectedId}/couriers`) : undefined}
                onUpdateStatus={updateOrderStatus}
                orders={orders}
              />
            ) : null}

            {tab === "tracking" && hasEstablishment && http ? <LiveDeliveries establishmentId={selectedId} http={http} /> : null}
            {tab === "kitchen" && hasEstablishment ? <Kitchen error={mutationError ?? dataError} onAdvance={updateOrderStatus} orders={orders} /> : null}

            {tab === "menu" && hasEstablishment ? (
              <AdminMenu
                busy={mutationBusy}
                error={mutationError ?? dataError}
                loading={dataLoading}
                menu={menu}
                onRefresh={refreshMenu}
                onSaveCategory={saveCategory}
                onSaveItem={saveItem}
                onSetCategoryActive={setCategoryActive}
                onSetItemActive={setItemActive}
                onSetItemAvailability={setItemAvailability}
              />
            ) : null}

            {tab === "inventory" && hasEstablishment && http ? <Inventory canImportNfe={session.modules.includes("fiscal")} establishmentId={selectedId} http={http} menu={menu} /> : null}
            {tab === "customers" && hasEstablishment && http ? <Customers establishmentId={selectedId} http={http} /> : null}
            {tab === "distribution" && hasEstablishment && http ? <Distribution establishmentId={selectedId} http={http} modules={session.modules} /> : null}
            {tab === "finance" && hasEstablishment && http ? <Finance establishmentId={selectedId} http={http} /> : null}
            {tab === "fiscal" && hasEstablishment && http ? <Fiscal establishmentId={selectedId} http={http} modules={session.modules} onOpenInventory={() => goTo("inventory")} /> : null}

            {tab === "whatsapp" && hasEstablishment && http ? (
              <Zest
                canInbox={session.modules.includes("whatsapp")}
                canTeach={session.modules.includes("ia") && canManage}
                establishmentId={selectedId}
                http={http}
                storeName={selectedEstablishment?.name ?? "Sua loja"}
              />
            ) : null}

            {tab === "settings" ? (
              <Settings
                company={
                  <AdminEstablishment
                    busy={mutationBusy}
                    error={mutationError}
                    establishment={selectedEstablishment}
                    onCreate={createEstablishment}
                    onUpdate={updateEstablishment}
                    tenantId={session.tenantId}
                  />
                }
                establishmentId={selectedId}
                http={http}
                isOwner={session.role === "Owner"}
                modules={session.modules}
              />
            ) : null}
          </main>
        </div>
      </div>
    </AdminContext.Provider>
  );
}
