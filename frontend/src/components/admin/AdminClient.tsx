"use client";

import Link from "next/link";
import { Fragment, useCallback, useEffect, useMemo, useState } from "react";
import { ApiError } from "@/lib/api";
import { UNAUTHORIZED_EVENT, adminApi, createAdminHttp, loadModules, login } from "@/lib/admin-api";
import { clearAdminSession, loadAdminSession, saveAdminSession } from "@/lib/admin-session";
import { completeVelosoId, endVelosoId, enterThroughPortal, isVelosoIdReturn, loadVelosoOptions, markVelosoLeft, startVelosoId, type VelosoOptions } from "@/lib/veloso-id";
import type {
  AdminOrder,
  AdminSession,
  CategoryPayload,
  CreateEstablishmentPayload,
  Establishment,
  ItemPrepStatus,
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
import { type CourierContact } from "./AdminOrders";
import { OrdersBoard, type TrackResult } from "./OrdersBoard";
import { AdminContext } from "./AdminContext";
import "./admin.css";
import "./admin-theme.css";
import "./admin-neon.css";
import "./admin-hud.css";
import { ColorDots } from "./ColorDots";
import { Distribution } from "./modules/Distribution";
import { Finance } from "./modules/Finance";
import { Inventory } from "./modules/Inventory";
import { Fiscal } from "./modules/Fiscal";
import type { ManualDraft } from "./modules/FiscalExtras";
import { Kitchen } from "./modules/Kitchen";
import { Overview } from "./modules/Overview";
import { Pdv } from "./modules/Pdv";
import { ZestIa } from "./modules/ZestIa";
import { Integrations } from "./modules/Integrations";
import { Customers, Suppliers } from "./modules/Parties";
import { Products } from "./modules/Products";
import { Icon } from "./Icon";
import { Settings } from "./modules/Settings";

type Tab = "overview" | "orders" | "kitchen" | "pdv" | "distribution" | "products" | "menu" | "customers" | "suppliers" | "inventory" | "finance" | "fiscal" | "reports" | "zest" | "integrations" | "settings";

const roleName: Record<UserRole, string> = { Owner: "Dono", Manager: "Gerente", Attendant: "Atendente", Kitchen: "Cozinha" };

const management: UserRole[] = ["Owner", "Manager"];
const everyone: UserRole[] = ["Owner", "Manager", "Attendant", "Kitchen"];

const tabs: Array<{ id: Tab; group: string; label: string; roles: UserRole[]; modules?: string[]; locked?: boolean }> = [
  { id: "overview", group: "Operação", label: "Visão geral", roles: ["Owner", "Manager", "Attendant"] },
  { id: "orders", group: "Operação", label: "Pedidos", roles: everyone, modules: ["pedidos"] },
  { id: "kitchen", group: "Operação", label: "Cozinha", roles: everyone, modules: ["pedidos"] },
  { id: "pdv", group: "Operação", label: "PDV", roles: ["Owner", "Manager", "Attendant"], modules: ["pedidos"] },
  { id: "distribution", group: "Operação", label: "Distribuição", roles: ["Owner", "Manager", "Attendant"], modules: ["pedidos"] },
  { id: "products", group: "Cadastros", label: "Produtos", roles: management, modules: ["cardapio"] },
  { id: "menu", group: "Cadastros", label: "Cardápio", roles: management, modules: ["cardapio"] },
  { id: "customers", group: "Cadastros", label: "Clientes", roles: ["Owner", "Manager", "Attendant"], modules: ["clientes"] },
  { id: "suppliers", group: "Cadastros", label: "Fornecedores", roles: management, modules: ["estoque"] },
  { id: "inventory", group: "Gestão", label: "Estoque", roles: management, modules: ["estoque"] },
  { id: "finance", group: "Gestão", label: "Financeiro", roles: management, modules: ["financeiro"] },
  { id: "fiscal", group: "Gestão", label: "Fiscal", roles: management, modules: ["fiscal", "fiscal_nfce"] },
  { id: "reports", group: "Gestão", label: "Relatórios", roles: management, modules: ["pedidos"], locked: true },
  { id: "zest", group: "Inteligência", label: "ZEST IA", roles: management, modules: ["ia"] },
  { id: "integrations", group: "Ajustes", label: "Integrações", roles: management },
  { id: "settings", group: "Ajustes", label: "Configurações", roles: management },
];

/**
 * Produtos do ecossistema dentro do painel (Escopo Mestre, seção 6 e 7): a operação do restaurante,
 * a Central Fiscal e a ZEST ficam separadas. Cada uma mostra só o seu menu.
 */
type Product = "operacao" | "fiscal" | "zest";
const productOf = (tab: Tab): Product => (tab === "fiscal" ? "fiscal" : tab === "zest" ? "zest" : "operacao");
const products: Array<{ id: Product; label: string; icon: string }> = [
  { id: "operacao", label: "Operação", icon: "establishment" },
  { id: "fiscal", label: "Fiscal", icon: "fiscal" },
  { id: "zest", label: "ZEST", icon: "zest" },
];
const tabIcons: Record<Tab, string> = {
  overview: "overview", orders: "orders", kitchen: "kitchen", pdv: "pos", distribution: "truck",
  products: "box", menu: "menu", customers: "customers", suppliers: "parties",
  inventory: "inventory", finance: "finance", fiscal: "fiscal", reports: "reports", zest: "zest", integrations: "plug", settings: "settings",
};

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
  const [velosoOptions, setVelosoOptions] = useState<VelosoOptions>({ velosoId: false, passwordLogin: false });
  const [establishments, setEstablishments] = useState<Establishment[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [tab, setTab] = useState<Tab>("overview");
  /* Ida e volta entre Compras e a Central Fiscal: a nota baixada vira entrada, a compra vira devolução/ajuste. */
  const [fiscalXml, setFiscalXml] = useState<string | null>(null);
  const [fiscalDraft, setFiscalDraft] = useState<ManualDraft | null>(null);
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

  const goToPortal = useCallback(() => {
    setBooting(true);
    void enterThroughPortal().then((leaving) => {
      if (!leaving) {
        setBooting(false);
      }
    });
  }, []);

  useEffect(() => {
    void loadVelosoOptions().then(setVelosoOptions);
  }, []);

  useEffect(() => {
    if (isVelosoIdReturn()) {
      void completeVelosoId()
        .then(async (next) => {
          saveAdminSession(next);
          setSession(next);
          setTab(next.role === "Kitchen" ? "kitchen" : "overview");
          await loadWorkspace(next);
        })
        .catch((error: unknown) => setAuthError(messageOf(error)))
        .finally(() => setBooting(false));
      return;
    }

    const stored = loadAdminSession();

    if (!stored) {
      goToPortal();
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
  }, [goToPortal, loadWorkspace]);

  useEffect(() => {
    const expire = () => {
      clearAdminSession();
      setSession(null);
      goToPortal();
    };

    window.addEventListener(UNAUTHORIZED_EVENT, expire);
    return () => window.removeEventListener(UNAUTHORIZED_EVENT, expire);
  }, [goToPortal]);

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
    if (!session || !selectedId || (tab !== "orders" && tab !== "kitchen" && tab !== "overview" && tab !== "pdv" && tab !== "distribution")) {
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
    const ending = session ? endVelosoId(session) : Promise.resolve();

    markVelosoLeft();

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
    setBooting(true);
    void ending.then(goToPortal);
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

  async function advanceItem(orderId: string, itemId: string, status: ItemPrepStatus) {
    if (!http) {
      return;
    }

    await runMutation(async () => {
      await http.patch(`/orders/${orderId}/items/${itemId}/prep`, { status });
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
    return (
      <AdminLogin
        busy={authBusy}
        error={authError}
        onLogin={handleLogin}
        onVelosoId={velosoOptions.velosoId ? () => void startVelosoId().catch((error: unknown) => setAuthError(messageOf(error))) : undefined}
        passwordLogin={velosoOptions.passwordLogin}
      />
    );
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

  const activeProduct = productOf(tab);
  const productTabs = visibleTabs.filter((item) => productOf(item.id) === activeProduct);
  function openProduct(product: Product) {
    const first = visibleTabs.find((item) => productOf(item.id) === product);
    if (first) {
      goTo(first.id);
    }
  }

  return (
    <AdminContext.Provider value={{ http, establishmentId: selectedId }}>
      <div className={`vx ${navOpen ? "is-open" : ""}`}>
        <aside className="vx-side">
          <Link className="vx-brand" href="/">
            <span className="vx-mark">TR</span>
            <div>
              <strong>To-No-<em>Rango</em></strong>
              <small>Por Veloso Solution</small>
            </div>
          </Link>
          <div aria-label="Produtos" className="vx-products" role="tablist">
            {products.map((product) => {
              const available = visibleTabs.some((item) => productOf(item.id) === product.id);
              return (
                <button
                  aria-selected={activeProduct === product.id}
                  className={`vx-product vx-product-${product.id} ${activeProduct === product.id ? "is-active" : ""}`}
                  disabled={!available || (!hasEstablishment && product.id !== "operacao")}
                  key={product.id}
                  onClick={() => openProduct(product.id)}
                  role="tab"
                  title={available ? product.label : `${product.label} não está no seu contrato`}
                  type="button"
                >
                  <Icon name={product.icon} size={17} />
                  <span>{product.label}</span>
                </button>
              );
            })}
          </div>
          <nav aria-label="Áreas do painel" className="vx-nav">
            {productTabs.map((item, index) => (
              <Fragment key={item.id}>
                {item.group !== productTabs[index - 1]?.group ? <p className="vx-sec">{item.group}</p> : null}
                <button
                  aria-current={tab === item.id ? "page" : undefined}
                  className={tab === item.id ? "is-active" : ""}
                  disabled={!hasEstablishment && item.id !== "settings"}
                  onClick={() => goTo(item.id)}
                  type="button"
                >
                  <i className="vx-orb"><Icon name={tabIcons[item.id]} size={16} /></i>
                  <span>{item.label}</span>
                  {item.id === "orders" && pendingOrders > 0 ? <b>{pendingOrders}</b> : null}
                  {item.locked ? <i className="vx-lock" title="Em levantamento de requisitos"><Icon name="lock" size={14} /></i> : null}
                </button>
              </Fragment>
            ))}
          </nav>
          <ColorDots />
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
              {pendingOrders > 0 ? <button className="vx-pill vx-pill-hot" onClick={() => goTo("orders")} type="button"><i />{pendingOrders} {pendingOrders === 1 ? "novo" : "novos"}</button> : null}
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
              <OrdersBoard
                error={mutationError ?? dataError}
                onLoadCouriers={http && session.role !== "Kitchen" && session.modules.includes("rastreio") ? () => http.get<CourierContact[]>(`/establishments/${selectedId}/couriers`) : undefined}
                onMarkPaid={session.role !== "Kitchen" ? markPaid : undefined}
                onOpenMap={session.role !== "Kitchen" && session.modules.includes("rastreio") ? () => goTo("distribution") : undefined}
                onRefund={canManage && session.modules.includes("pagamentos") ? refundOrder : undefined}
                onTrack={http && session.role !== "Kitchen" && session.modules.includes("rastreio") ? (orderId, courierId) => http.post<TrackResult>(`/orders/${orderId}/courier`, { courierId: courierId ?? null }) : undefined}
                onUpdateStatus={updateOrderStatus}
                orders={orders}
              />
            ) : null}

            {tab === "kitchen" && hasEstablishment ? <Kitchen error={mutationError ?? dataError} onAdvance={updateOrderStatus} onAdvanceItem={http ? advanceItem : undefined} orders={orders} /> : null}
            {tab === "pdv" && hasEstablishment && http ? <Pdv canManage={canManage} establishmentId={selectedId} http={http} menu={menu} onChanged={refreshOrders} orders={orders} /> : null}
            {tab === "fiscal" && hasEstablishment && http ? (
              <Fiscal
                draft={fiscalDraft}
                establishmentId={selectedId}
                http={http}
                key={fiscalDraft?.referencedKey ?? "fiscal"}
                modules={session.modules}
                onOpenInventory={(xml) => { setFiscalXml(xml ?? null); setTab("inventory"); }}
              />
            ) : null}
            {tab === "reports" && hasEstablishment ? (
              <section className="locked-module">
                <span className="locked-icon"><Icon name="lock" size={34} /></span>
                <p className="eyebrow">Relatórios</p>
                <h2>Em levantamento de requisitos</h2>
                <p className="muted">Os relatórios estão sendo redefinidos. Enquanto isso, os números do dia estão na Visão geral, no Financeiro e no Estoque.</p>
              </section>
            ) : null}
            {tab === "zest" && hasEstablishment && http ? <ZestIa establishmentId={selectedId} http={http} /> : null}

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

            {tab === "inventory" && hasEstablishment && http ? (
              <Inventory
                canImportNfe={session.modules.includes("fiscal")}
                establishmentId={selectedId}
                http={http}
                initialXml={fiscalXml}
                key={fiscalXml ? "from-fiscal" : "inventory"}
                menu={menu}
                onOpenFiscal={session.modules.some((item) => item === "fiscal" || item === "fiscal_nfce") ? (draft) => { setFiscalDraft(draft ?? null); setFiscalXml(null); setTab("fiscal"); } : undefined}
              />
            ) : null}
            {tab === "products" && hasEstablishment && http ? <Products establishmentId={selectedId} http={http} menu={menu} modules={session.modules} onMenuChanged={refreshMenu} /> : null}
            {tab === "suppliers" && hasEstablishment && http ? <Suppliers establishmentId={selectedId} http={http} /> : null}
            {tab === "customers" && hasEstablishment && http ? <Customers establishmentId={selectedId} http={http} /> : null}
            {tab === "distribution" && hasEstablishment && http ? (
              <Distribution
                establishmentId={selectedId}
                http={http}
                modules={session.modules}
                onChanged={refreshOrders}
                onTrack={session.modules.includes("rastreio") ? (orderId, courierId) => http.post<TrackResult>(`/orders/${orderId}/courier`, { courierId }) : undefined}
                orders={orders}
              />
            ) : null}
            {tab === "finance" && hasEstablishment && http ? <Finance establishmentId={selectedId} http={http} /> : null}
            {tab === "integrations" && hasEstablishment && http ? (
              <Integrations establishmentId={selectedId} http={http} modules={session.modules} onOpen={(target) => goTo(target as Tab)} />
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
