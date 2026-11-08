"use client";

import { formatBRL, type AdminHttp } from "@/lib/admin-api";
import { Empty, ErrorMessage, ModuleHeading, useLoad } from "./shared";

interface Purchase {
  id: string;
  invoiceNumber: string | null;
  accessKey: string | null;
  issuedOn: string;
  totalInCents: number;
}

export function Fiscal({ http, establishmentId, onOpenInventory }: { http: AdminHttp; establishmentId: string; onOpenInventory: () => void }) {
  const purchases = useLoad(() => http.get<Purchase[]>(`/establishments/${establishmentId}/purchases`).catch(() => []), [establishmentId]);
  const notes = purchases.data?.filter((purchase) => purchase.accessKey) ?? [];

  return (
    <section className="admin-section">
      <ModuleHeading eyebrow="Fiscal" title="Fiscal" hint="Notas de entrada importadas por XML." />
      <div><button className="primary-button" onClick={onOpenInventory} type="button">Importar XML de NF-e</button></div>
      <ErrorMessage message={purchases.error} />
      {purchases.data && notes.length === 0 ? <Empty>Nenhuma NF-e de entrada importada.</Empty> : null}
      {notes.length > 0 ? (
        <div className="card admin-table-wrap">
          <table className="admin-table">
            <thead><tr><th>Número</th><th>Emissão</th><th>Chave de acesso</th><th className="num">Total</th></tr></thead>
            <tbody>
              {notes.map((note) => (
                <tr key={note.id}><td>{note.invoiceNumber ?? "—"}</td><td>{note.issuedOn.split("-").reverse().join("/")}</td><td>{note.accessKey}</td><td className="num">{formatBRL(note.totalInCents)}</td></tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
      <article className="card vx-box">
        <div className="vx-panel-title"><h3>Ainda não disponível</h3></div>
        <ul className="vx-unavailable">
          <li><strong>Emissão de NF-e e NFC-e</strong><span>Depende de ACBr, certificado A1 e homologação na SEFAZ.</span></li>
          <li><strong>SINTEGRA</strong><span>Geração do arquivo ainda não implementada.</span></li>
          <li><strong>SPED Fiscal e SPED Contribuições</strong><span>Geração e validação no PVA ainda não implementadas.</span></li>
        </ul>
      </article>
    </section>
  );
}
