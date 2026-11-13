/**
 * DANFE (NF-e, retrato) e DANFE NFC-e (bobina 80 mm) a partir do XML autorizado, abertos numa janela
 * pronta para imprimir ou "Salvar como PDF". Representação simplificada do leiaute do MOC: confira
 * com o contador antes de usar como documento auxiliar em fiscalização.
 */

const NS = "http://www.portalfiscal.inf.br/nfe";

/** O XML guardado pode trazer eventos (cancelamento, carta de correção) colados depois da nota. */
function onlyNote(xml: string): string {
  const cut = xml.search(/\n<!-- (evento|carta)/);
  return cut > 0 ? xml.slice(0, cut) : xml;
}

const esc = (value: string | null | undefined) =>
  String(value ?? "").replace(/[&<>"']/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]!);

const money = (value: string | null | undefined) => Number(value ?? 0).toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const qty = (value: string | null | undefined) => Number(value ?? 0).toLocaleString("pt-BR", { maximumFractionDigits: 4 });
const keyGroups = (key: string) => key.replace(/(\d{4})(?=\d)/g, "$1 ");
const docFmt = (value: string) =>
  value.length === 14 ? value.replace(/(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})/, "$1.$2.$3/$4-$5") : value.length === 11 ? value.replace(/(\d{3})(\d{3})(\d{3})(\d{2})/, "$1.$2.$3-$4") : value;
const dateTime = (value: string | null | undefined) => (value ? new Date(value).toLocaleString("pt-BR") : "");

/** Código de barras CODE-128 C da chave (44 dígitos, pares numéricos), em SVG. */
export function code128c(digits: string): string {
  const patterns = [
    "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213", "221312", "231212", "112232", "122132", "122231", "113222",
    "123122", "123221", "223211", "221132", "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211", "212123", "212321",
    "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313", "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121",
    "313121", "211331", "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111", "314111", "221411", "431111", "111224",
    "111422", "121124", "121421", "141122", "141221", "112214", "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
    "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141", "214121", "412121", "111143", "111341", "131141", "114113",
    "114311", "411113", "411311", "113141", "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
  ];
  const codes = [105];
  for (let at = 0; at < digits.length; at += 2) codes.push(Number(digits.slice(at, at + 2)));
  const check = codes.reduce((sum, code, index) => sum + code * (index === 0 ? 1 : index), 0) % 103;
  codes.push(check, 106);
  let x = 10;
  const bars: string[] = [];
  for (const code of codes) {
    [...patterns[code]].forEach((width, index) => {
      const w = Number(width);
      if (index % 2 === 0) bars.push(`<rect x="${x}" y="0" width="${w}" height="50"/>`);
      x += w;
    });
  }
  return `<svg class="barcode" viewBox="0 0 ${x + 10} 50" preserveAspectRatio="none" xmlns="http://www.w3.org/2000/svg">${bars.join("")}</svg>`;
}

function read(xml: string) {
  const doc = new DOMParser().parseFromString(onlyNote(xml), "application/xml");
  if (doc.getElementsByTagName("parsererror").length) throw new Error("XML da nota ilegível.");
  const one = (parent: Element | Document | null | undefined, name: string) => (parent ? parent.getElementsByTagNameNS(NS, name)[0] ?? null : null);
  const val = (parent: Element | Document | null | undefined, name: string) => one(parent, name)?.textContent?.trim() ?? "";
  return { doc, one, val };
}

const PRINT_CSS = `
*{box-sizing:border-box} body{font:10px/1.25 Arial,Helvetica,sans-serif;color:#000;margin:0;background:#fff}
.page{width:200mm;margin:6mm auto} .box{border:1px solid #000;padding:2px 4px;min-height:24px} .box small{display:block;font-size:7px;text-transform:uppercase}
.row{display:flex} .row>.box{flex:1;border-right:none} .row>.box:last-child{border-right:1px solid #000} .row+.row>.box,.stack>.box+.box{border-top:none}
h1{font-size:13px;margin:0} h2{font-size:9px;margin:6px 0 2px;text-transform:uppercase} .c{text-align:center} .r{text-align:right} .b{font-weight:700}
table{width:100%;border-collapse:collapse} th,td{border:1px solid #000;padding:2px 3px;font-size:8px;vertical-align:top} th{font-size:7px;text-transform:uppercase}
.barcode{width:100%;height:38px;fill:#000} .stamp{font-size:20px;color:#b00;border:3px solid #b00;padding:4px 10px;display:inline-block;transform:rotate(-8deg);margin:6px}
.bar{position:sticky;top:0;background:#111;color:#fff;padding:8px;text-align:center;font:14px Arial} .bar button{font:14px Arial;padding:6px 16px;margin-left:8px;cursor:pointer}
.cupom{width:80mm;margin:4mm auto;font:11px/1.3 "Courier New",monospace} .cupom hr{border:none;border-top:1px dashed #000} .cupom .qr{display:flex;justify-content:center;margin:6px 0} .cupom .qr img,.cupom .qr canvas{width:38mm!important;height:38mm!important;image-rendering:pixelated}
@media print{.bar{display:none} .page,.cupom{margin:0}}
`;

function openWindow(title: string, body: string, extraHead = "") {
  const win = window.open("", "_blank", "width=900,height=1000");
  if (!win) throw new Error("O navegador bloqueou a janela. Libere pop-ups para este site.");
  win.document.write(`<!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><title>${esc(title)}</title><style>${PRINT_CSS}</style>${extraHead}</head><body>
<div class="bar">${esc(title)} <button onclick="print()">Imprimir / Salvar PDF</button></div>${body}</body></html>`);
  win.document.close();
}

export function openDanfe(xml: string, options: { cancelled?: boolean; homologation?: boolean } = {}) {
  const { doc, one, val } = read(xml);
  const ide = one(doc, "ide"), emit = one(doc, "emit"), dest = one(doc, "dest"), total = one(doc, "ICMSTot"), prot = one(doc, "infProt");
  const enderEmit = one(emit, "enderEmit"), enderDest = one(dest, "enderDest");
  const key = (one(doc, "infNFe")?.getAttribute("Id") ?? "").replace(/\D/g, "");
  const model = val(ide, "mod");
  if (model === "65") return openDanfeNfce(xml, options);

  const items = Array.from(doc.getElementsByTagNameNS(NS, "det")).map((det) => {
    const prod = one(det, "prod");
    const icms = one(det, "ICMS")?.firstElementChild ?? null;
    const cst = val(icms, "CST") || val(icms, "CSOSN");
    return `<tr><td>${esc(val(prod, "cProd"))}</td><td>${esc(val(prod, "xProd"))}</td><td>${esc(val(prod, "NCM"))}</td><td class="c">${esc(val(icms, "orig"))}${esc(cst)}</td><td class="c">${esc(val(prod, "CFOP"))}</td><td class="c">${esc(val(prod, "uCom"))}</td><td class="r">${qty(val(prod, "qCom"))}</td><td class="r">${money(val(prod, "vUnCom"))}</td><td class="r">${money(val(prod, "vProd"))}</td><td class="r">${money(val(icms, "vBC"))}</td><td class="r">${money(val(icms, "vICMS"))}</td><td class="r">${val(icms, "pICMS") ? money(val(icms, "pICMS")) : ""}</td></tr>`;
  }).join("");
  const purpose = { "2": "NF-e complementar", "3": "NF-e de ajuste", "4": "Devolução" }[val(ide, "finNFe")] ?? "";
  const refs = Array.from(doc.getElementsByTagNameNS(NS, "refNFe")).map((ref) => keyGroups(ref.textContent ?? "")).join("<br>");
  const destDoc = val(dest, "CNPJ") || val(dest, "CPF");
  const homolog = options.homologation || val(ide, "tpAmb") === "2";

  const body = `<div class="page">
  <div class="row"><div class="box" style="flex:3"><small>Recebemos de ${esc(val(emit, "xNome"))} os produtos/serviços constantes da nota fiscal indicada ao lado</small><br>Data de recebimento: ____/____/______ &nbsp; Identificação e assinatura do recebedor: ____________________________</div>
  <div class="box c"><b>NF-e</b><br>Nº ${esc(val(ide, "nNF"))}<br>Série ${esc(val(ide, "serie"))}</div></div>
  <h2>&nbsp;</h2>
  <div class="row">
    <div class="box" style="flex:3"><h1>${esc(val(emit, "xNome"))}</h1>${esc(val(enderEmit, "xLgr"))}, ${esc(val(enderEmit, "nro"))} - ${esc(val(enderEmit, "xBairro"))}<br>${esc(val(enderEmit, "xMun"))} - ${esc(val(enderEmit, "UF"))} CEP ${esc(val(enderEmit, "CEP"))}</div>
    <div class="box c" style="flex:1.4"><b style="font-size:13px">DANFE</b><br>Documento Auxiliar da Nota Fiscal Eletrônica<br>${val(ide, "tpNF") === "0" ? "0 - ENTRADA" : "1 - SAÍDA"}<br><b>Nº ${esc(val(ide, "nNF"))} · Série ${esc(val(ide, "serie"))}</b>${purpose ? `<br><b>${esc(purpose)}</b>` : ""}</div>
    <div class="box" style="flex:3">${code128c(key)}<small>Chave de acesso</small><b>${keyGroups(key)}</b><br>Consulta de autenticidade no portal nacional da NF-e (www.nfe.fazenda.gov.br/portal) ou no site da SEFAZ autorizadora</div>
  </div>
  <div class="row"><div class="box" style="flex:3"><small>Natureza da operação</small>${esc(val(ide, "natOp"))}</div><div class="box" style="flex:2"><small>Protocolo de autorização de uso</small>${esc(val(prot, "nProt"))} ${esc(dateTime(val(prot, "dhRecbto")))}</div></div>
  <div class="row"><div class="box"><small>Inscrição estadual</small>${esc(val(emit, "IE"))}</div><div class="box"><small>CNPJ</small>${esc(docFmt(val(emit, "CNPJ")))}</div></div>
  ${refs ? `<div class="row"><div class="box"><small>Documento(s) referenciado(s)</small>${refs}</div></div>` : ""}
  <h2>Destinatário / remetente</h2>
  <div class="row"><div class="box" style="flex:3"><small>Nome / razão social</small>${esc(val(dest, "xNome"))}</div><div class="box"><small>CNPJ / CPF</small>${esc(docFmt(destDoc))}</div><div class="box"><small>Data da emissão</small>${esc(dateTime(val(ide, "dhEmi")))}</div></div>
  <div class="row"><div class="box" style="flex:3"><small>Endereço</small>${esc(val(enderDest, "xLgr"))}, ${esc(val(enderDest, "nro"))} - ${esc(val(enderDest, "xBairro"))}</div><div class="box"><small>Município</small>${esc(val(enderDest, "xMun"))}</div><div class="box"><small>UF</small>${esc(val(enderDest, "UF"))}</div><div class="box"><small>Inscrição estadual</small>${esc(val(dest, "IE"))}</div></div>
  <h2>Cálculo do imposto</h2>
  <div class="row"><div class="box"><small>Base de cálculo do ICMS</small>${money(val(total, "vBC"))}</div><div class="box"><small>Valor do ICMS</small>${money(val(total, "vICMS"))}</div><div class="box"><small>Base ICMS ST</small>${money(val(total, "vBCST"))}</div><div class="box"><small>Valor ICMS ST</small>${money(val(total, "vST"))}</div><div class="box"><small>Valor total dos produtos</small>${money(val(total, "vProd"))}</div></div>
  <div class="row"><div class="box"><small>Valor do frete</small>${money(val(total, "vFrete"))}</div><div class="box"><small>Desconto</small>${money(val(total, "vDesc"))}</div><div class="box"><small>Outras despesas</small>${money(val(total, "vOutro"))}</div><div class="box"><small>Valor do IPI</small>${money(val(total, "vIPI"))}</div><div class="box"><small>PIS / COFINS</small>${money(val(total, "vPIS"))} / ${money(val(total, "vCOFINS"))}</div><div class="box b"><small>Valor total da nota</small>${money(val(total, "vNF"))}</div></div>
  <h2>Dados dos produtos / serviços</h2>
  <table><thead><tr><th>Código</th><th>Descrição</th><th>NCM</th><th>CST</th><th>CFOP</th><th>Un</th><th>Qtd</th><th>V. unit.</th><th>V. total</th><th>BC ICMS</th><th>V. ICMS</th><th>Alíq. ICMS</th></tr></thead><tbody>${items}</tbody></table>
  <h2>Dados adicionais</h2>
  <div class="box" style="min-height:60px"><small>Informações complementares</small>${esc(val(one(doc, "infAdic"), "infCpl"))}</div>
  ${options.cancelled ? '<div class="c"><span class="stamp">NF-e CANCELADA</span></div>' : ""}
  ${homolog ? '<div class="c"><span class="stamp">SEM VALOR FISCAL — HOMOLOGAÇÃO</span></div>' : ""}
</div>`;
  openWindow(`DANFE NF-e ${val(ide, "nNF")}`, body);
}

export function openDanfeNfce(xml: string, options: { cancelled?: boolean; homologation?: boolean } = {}) {
  const { doc, one, val } = read(xml);
  const ide = one(doc, "ide"), emit = one(doc, "emit"), dest = one(doc, "dest"), total = one(doc, "ICMSTot"), prot = one(doc, "infProt"), supl = one(doc, "infNFeSupl");
  const enderEmit = one(emit, "enderEmit");
  const key = (one(doc, "infNFe")?.getAttribute("Id") ?? "").replace(/\D/g, "");
  const qrUrl = val(supl, "qrCode");
  const items = Array.from(doc.getElementsByTagNameNS(NS, "det")).map((det, index) => {
    const prod = one(det, "prod");
    return `${String(index + 1).padStart(3, "0")} ${esc(val(prod, "xProd"))}<br>&nbsp;&nbsp;${qty(val(prod, "qCom"))} ${esc(val(prod, "uCom"))} x ${money(val(prod, "vUnCom"))} = <b>${money(val(prod, "vProd"))}</b>`;
  }).join("<br>");
  const payments = Array.from(doc.getElementsByTagNameNS(NS, "detPag")).map((pay) => {
    const kind = { "01": "Dinheiro", "03": "Cartão de crédito", "04": "Cartão de débito", "17": "PIX", "90": "Sem pagamento", "99": "Outros" }[val(pay, "tPag")] ?? val(pay, "tPag");
    return `${esc(kind)}: ${money(val(pay, "vPag"))}`;
  }).join("<br>");
  const homolog = options.homologation || val(ide, "tpAmb") === "2";
  const body = `<div class="cupom">
  <div class="c"><b>${esc(val(emit, "xNome"))}</b><br>CNPJ ${esc(docFmt(val(emit, "CNPJ")))} IE ${esc(val(emit, "IE"))}<br>${esc(val(enderEmit, "xLgr"))}, ${esc(val(enderEmit, "nro"))} - ${esc(val(enderEmit, "xMun"))}/${esc(val(enderEmit, "UF"))}</div><hr>
  <div class="c b">DANFE NFC-e - Documento Auxiliar da Nota Fiscal de Consumidor Eletrônica</div><hr>
  ${items}<hr>
  Qtd. total de itens: ${doc.getElementsByTagNameNS(NS, "det").length}<br>Desconto: ${money(val(total, "vDesc"))}<br><b>VALOR A PAGAR R$ ${money(val(total, "vNF"))}</b><br>${payments}${val(one(doc, "pag"), "vTroco") ? `<br>Troco: ${money(val(one(doc, "pag"), "vTroco"))}` : ""}<hr>
  <div class="c">Consulte pela chave de acesso em<br>${esc(val(supl, "urlChave"))}<br><b>${keyGroups(key)}</b></div><hr>
  <div class="c">${dest ? `CONSUMIDOR ${esc(docFmt(val(dest, "CPF") || val(dest, "CNPJ")))} ${esc(val(dest, "xNome"))}` : "CONSUMIDOR NÃO IDENTIFICADO"}</div><hr>
  <div class="c">NFC-e nº ${esc(val(ide, "nNF"))} Série ${esc(val(ide, "serie"))} ${esc(dateTime(val(ide, "dhEmi")))}<br>Protocolo de autorização: ${esc(val(prot, "nProt"))}<br>Data de autorização: ${esc(dateTime(val(prot, "dhRecbto")))}</div>
  <div class="qr" id="qr"></div>
  ${options.cancelled ? '<div class="c"><span class="stamp">CANCELADA</span></div>' : ""}
  ${homolog ? '<div class="c b">EMITIDA EM AMBIENTE DE HOMOLOGAÇÃO - SEM VALOR FISCAL</div>' : ""}
</div>
<script>window.addEventListener("load",function(){ if(window.QRCode){ new QRCode(document.getElementById("qr"),{text:${JSON.stringify(qrUrl)},width:360,height:360,correctLevel:QRCode.CorrectLevel.M}); } else { document.getElementById("qr").textContent=${JSON.stringify(qrUrl)}; } });</script>`;
  openWindow(`DANFE NFC-e ${val(ide, "nNF")}`, body, '<script src="https://cdnjs.cloudflare.com/ajax/libs/qrcodejs/1.0.0/qrcode.min.js"></script>');
}
