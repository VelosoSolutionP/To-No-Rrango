namespace ToNoRango.Application.Fiscal;

public sealed record FiscalCode(string Code, string Description);

public sealed record FiscalTablesDto(
    IReadOnlyList<FiscalCode> Cfop,
    IReadOnlyList<FiscalCode> IcmsCst,
    IReadOnlyList<FiscalCode> Csosn,
    IReadOnlyList<FiscalCode> PisCofinsCst,
    IReadOnlyList<FiscalCode> IpiCst,
    IReadOnlyList<FiscalCode> Origin);

/// <summary>
/// Tabelas de referência (Ajuste SINIEF 07/01 para CFOP; Convênio s/nº de 1970 e Ajuste SINIEF 03/10
/// para CST/CSOSN; IN RFB 1.009/2010 para PIS/COFINS e IPI). Os grupos 2 e 6 repetem os 1 e 5 em
/// operação interestadual, como no próprio Ajuste. Conferir com o contador antes de usar.
/// </summary>
public static class FiscalTables
{
    private static readonly (string Code, string Description)[] Inbound =
    [
        ("101", "Compra para industrialização ou produção rural"),
        ("102", "Compra para comercialização"),
        ("111", "Compra para industrialização de mercadoria recebida anteriormente em consignação industrial"),
        ("113", "Compra para comercialização, de mercadoria recebida anteriormente em consignação mercantil"),
        ("116", "Compra para industrialização ou produção rural originada de encomenda para recebimento futuro"),
        ("117", "Compra para comercialização originada de encomenda para recebimento futuro"),
        ("118", "Compra de mercadoria para comercialização pelo adquirente originário, entregue pelo vendedor remetente ao destinatário, em venda à ordem"),
        ("120", "Compra para industrialização, em venda à ordem, já recebida do vendedor remetente"),
        ("121", "Compra para comercialização, em venda à ordem, já recebida do vendedor remetente"),
        ("122", "Compra para industrialização em que a mercadoria foi remetida pelo fornecedor ao industrializador sem transitar pelo estabelecimento adquirente"),
        ("124", "Industrialização efetuada por outra empresa"),
        ("125", "Industrialização efetuada por outra empresa quando a mercadoria remetida para utilização no processo de industrialização não transitou pelo estabelecimento adquirente"),
        ("126", "Compra para utilização na prestação de serviço sujeita ao ICMS"),
        ("128", "Compra para utilização na prestação de serviço sujeita ao ISSQN"),
        ("151", "Transferência para industrialização ou produção rural"),
        ("152", "Transferência para comercialização"),
        ("153", "Transferência de energia elétrica para distribuição"),
        ("154", "Transferência para utilização na prestação de serviço"),
        ("201", "Devolução de venda de produção do estabelecimento"),
        ("202", "Devolução de venda de mercadoria adquirida ou recebida de terceiros"),
        ("203", "Devolução de venda de produção do estabelecimento, destinada à Zona Franca de Manaus ou Áreas de Livre Comércio"),
        ("204", "Devolução de venda de mercadoria adquirida ou recebida de terceiros, destinada à Zona Franca de Manaus ou Áreas de Livre Comércio"),
        ("208", "Devolução de produção do estabelecimento, remetida em transferência"),
        ("209", "Devolução de mercadoria adquirida ou recebida de terceiros, remetida em transferência"),
        ("251", "Compra de energia elétrica para distribuição ou comercialização"),
        ("252", "Compra de energia elétrica por estabelecimento industrial"),
        ("253", "Compra de energia elétrica por estabelecimento comercial"),
        ("256", "Compra de energia elétrica por estabelecimento de produtor rural"),
        ("257", "Compra de energia elétrica para consumo por demanda contratada"),
        ("301", "Aquisição de serviço de comunicação para execução de serviço da mesma natureza"),
        ("302", "Aquisição de serviço de comunicação por estabelecimento industrial"),
        ("303", "Aquisição de serviço de comunicação por estabelecimento comercial"),
        ("351", "Aquisição de serviço de transporte para execução de serviço da mesma natureza"),
        ("352", "Aquisição de serviço de transporte por estabelecimento industrial"),
        ("353", "Aquisição de serviço de transporte por estabelecimento comercial"),
        ("354", "Aquisição de serviço de transporte por estabelecimento de prestador de serviço de comunicação"),
        ("356", "Aquisição de serviço de transporte por estabelecimento de produtor rural"),
        ("401", "Compra para industrialização ou produção rural em operação com mercadoria sujeita ao regime de substituição tributária"),
        ("403", "Compra para comercialização em operação com mercadoria sujeita ao regime de substituição tributária"),
        ("406", "Compra de bem para o ativo imobilizado cuja mercadoria está sujeita ao regime de substituição tributária"),
        ("407", "Compra de mercadoria para uso ou consumo cuja mercadoria está sujeita ao regime de substituição tributária"),
        ("408", "Transferência para industrialização ou produção rural em operação com mercadoria sujeita ao regime de substituição tributária"),
        ("409", "Transferência para comercialização em operação com mercadoria sujeita ao regime de substituição tributária"),
        ("410", "Devolução de venda de produção do estabelecimento em operação com produto sujeito ao regime de substituição tributária"),
        ("411", "Devolução de venda de mercadoria adquirida ou recebida de terceiros em operação com mercadoria sujeita ao regime de substituição tributária"),
        ("414", "Retorno de produção do estabelecimento, remetida para venda fora do estabelecimento, em operação com produto sujeito ao regime de substituição tributária"),
        ("415", "Retorno de mercadoria adquirida ou recebida de terceiros, remetida para venda fora do estabelecimento em operação com mercadoria sujeita ao regime de substituição tributária"),
        ("451", "Retorno de animal do estabelecimento produtor"),
        ("452", "Retorno de insumo não utilizado na produção"),
        ("501", "Entrada de mercadoria recebida com fim específico de exportação"),
        ("503", "Entrada decorrente de devolução de produto remetido com fim específico de exportação, de produção do estabelecimento"),
        ("504", "Entrada decorrente de devolução de mercadoria remetida com fim específico de exportação, adquirida ou recebida de terceiros"),
        ("505", "Entrada decorrente de devolução simbólica de mercadorias remetidas para formação de lote de exportação, de produtos industrializados ou produzidos pelo próprio estabelecimento"),
        ("506", "Entrada decorrente de devolução simbólica de mercadorias, adquiridas ou recebidas de terceiros, remetidas para formação de lote de exportação"),
        ("551", "Compra de bem para o ativo imobilizado"),
        ("552", "Transferência de bem do ativo imobilizado"),
        ("553", "Devolução de venda de bem do ativo imobilizado"),
        ("554", "Retorno de bem do ativo imobilizado remetido para uso fora do estabelecimento"),
        ("555", "Entrada de bem do ativo imobilizado de terceiro, remetido para uso no estabelecimento"),
        ("556", "Compra de material para uso ou consumo"),
        ("557", "Transferência de material para uso ou consumo"),
        ("651", "Compra de combustível ou lubrificante para industrialização subsequente"),
        ("652", "Compra de combustível ou lubrificante para comercialização"),
        ("653", "Compra de combustível ou lubrificante por consumidor ou usuário final"),
        ("658", "Transferência de combustível e lubrificante para industrialização"),
        ("659", "Transferência de combustível e lubrificante para comercialização"),
        ("660", "Devolução de venda de combustível ou lubrificante destinado à industrialização subsequente"),
        ("661", "Devolução de venda de combustível ou lubrificante destinado à comercialização"),
        ("662", "Devolução de venda de combustível ou lubrificante destinado a consumidor ou usuário final"),
        ("663", "Entrada de combustível ou lubrificante para armazenagem"),
        ("664", "Retorno de combustível ou lubrificante remetido para armazenagem"),
        ("901", "Entrada para industrialização por encomenda"),
        ("902", "Retorno de mercadoria remetida para industrialização por encomenda"),
        ("903", "Entrada de mercadoria remetida para industrialização e não aplicada no referido processo"),
        ("904", "Retorno de remessa para venda fora do estabelecimento"),
        ("905", "Entrada de mercadoria recebida para depósito em depósito fechado ou armazém geral"),
        ("906", "Retorno de mercadoria remetida para depósito fechado ou armazém geral"),
        ("907", "Retorno simbólico de mercadoria remetida para depósito fechado ou armazém geral"),
        ("908", "Entrada de bem por conta de contrato de comodato"),
        ("909", "Retorno de bem remetido por conta de contrato de comodato"),
        ("910", "Entrada de bonificação, doação ou brinde"),
        ("911", "Entrada de amostra grátis"),
        ("912", "Entrada de mercadoria ou bem recebido para demonstração ou mostruário"),
        ("913", "Retorno de mercadoria ou bem remetido para demonstração, mostruário ou treinamento"),
        ("914", "Retorno de mercadoria ou bem remetido para exposição ou feira"),
        ("915", "Entrada de mercadoria ou bem recebido para conserto ou reparo"),
        ("916", "Retorno de mercadoria ou bem remetido para conserto ou reparo"),
        ("917", "Entrada de mercadoria recebida em consignação mercantil ou industrial"),
        ("918", "Devolução de mercadoria remetida em consignação mercantil ou industrial"),
        ("919", "Devolução simbólica de mercadoria vendida ou utilizada em processo industrial, remetida anteriormente em consignação mercantil ou industrial"),
        ("920", "Entrada de vasilhame ou sacaria"),
        ("921", "Retorno de vasilhame ou sacaria"),
        ("922", "Lançamento efetuado a título de simples faturamento decorrente de compra para recebimento futuro"),
        ("923", "Entrada de mercadoria recebida do vendedor remetente, em venda à ordem"),
        ("924", "Entrada para industrialização por conta e ordem do adquirente da mercadoria, quando esta não transitar pelo estabelecimento do adquirente"),
        ("925", "Retorno de mercadoria remetida para industrialização por conta e ordem do adquirente da mercadoria, quando esta não transitar pelo estabelecimento do adquirente"),
        ("949", "Outra entrada de mercadoria ou prestação de serviço não especificada"),
    ];

    private static readonly (string Code, string Description)[] Outbound =
    [
        ("101", "Venda de produção do estabelecimento"),
        ("102", "Venda de mercadoria adquirida ou recebida de terceiros"),
        ("103", "Venda de produção do estabelecimento, efetuada fora do estabelecimento"),
        ("104", "Venda de mercadoria adquirida ou recebida de terceiros, efetuada fora do estabelecimento"),
        ("105", "Venda de produção do estabelecimento que não deva por ele transitar"),
        ("106", "Venda de mercadoria adquirida ou recebida de terceiros, que não deva por ele transitar"),
        ("109", "Venda de produção do estabelecimento, destinada à Zona Franca de Manaus ou Áreas de Livre Comércio"),
        ("110", "Venda de mercadoria adquirida ou recebida de terceiros, destinada à Zona Franca de Manaus ou Áreas de Livre Comércio"),
        ("111", "Venda de produção do estabelecimento remetida anteriormente em consignação industrial"),
        ("112", "Venda de mercadoria adquirida ou recebida de terceiros remetida anteriormente em consignação industrial"),
        ("113", "Venda de produção do estabelecimento remetida anteriormente em consignação mercantil"),
        ("114", "Venda de mercadoria adquirida ou recebida de terceiros remetida anteriormente em consignação mercantil"),
        ("115", "Venda de mercadoria adquirida ou recebida de terceiros, recebida anteriormente em consignação mercantil"),
        ("116", "Venda de produção do estabelecimento originada de encomenda para entrega futura"),
        ("117", "Venda de mercadoria adquirida ou recebida de terceiros, originada de encomenda para entrega futura"),
        ("118", "Venda de produção do estabelecimento entregue ao destinatário por conta e ordem do adquirente originário, em venda à ordem"),
        ("119", "Venda de mercadoria adquirida ou recebida de terceiros entregue ao destinatário por conta e ordem do adquirente originário, em venda à ordem"),
        ("120", "Venda de mercadoria adquirida ou recebida de terceiros entregue ao destinatário pelo vendedor remetente, em venda à ordem"),
        ("122", "Venda de produção do estabelecimento remetida para industrialização, por conta e ordem do adquirente, sem transitar pelo estabelecimento do adquirente"),
        ("123", "Venda de mercadoria adquirida ou recebida de terceiros remetida para industrialização, por conta e ordem do adquirente, sem transitar pelo estabelecimento do adquirente"),
        ("124", "Industrialização efetuada para outra empresa"),
        ("125", "Industrialização efetuada para outra empresa quando a mercadoria recebida para utilização no processo de industrialização não transitar pelo estabelecimento adquirente"),
        ("151", "Transferência de produção do estabelecimento"),
        ("152", "Transferência de mercadoria adquirida ou recebida de terceiros"),
        ("153", "Transferência de energia elétrica"),
        ("155", "Transferência de produção do estabelecimento, que não deva por ele transitar"),
        ("156", "Transferência de mercadoria adquirida ou recebida de terceiros, que não deva por ele transitar"),
        ("201", "Devolução de compra para industrialização ou produção rural"),
        ("202", "Devolução de compra para comercialização"),
        ("205", "Anulação de valor relativo a aquisição de serviço de comunicação"),
        ("206", "Anulação de valor relativo a aquisição de serviço de transporte"),
        ("207", "Anulação de valor relativo à compra de energia elétrica"),
        ("208", "Devolução de mercadoria recebida em transferência para industrialização ou produção rural"),
        ("209", "Devolução de mercadoria recebida em transferência para comercialização"),
        ("210", "Devolução de compra para utilização na prestação de serviço"),
        ("251", "Venda de energia elétrica para distribuição ou comercialização"),
        ("252", "Venda de energia elétrica para estabelecimento industrial"),
        ("253", "Venda de energia elétrica para estabelecimento comercial"),
        ("258", "Venda de energia elétrica a não contribuinte"),
        ("301", "Prestação de serviço de comunicação para execução de serviço da mesma natureza"),
        ("307", "Prestação de serviço de comunicação a não contribuinte"),
        ("351", "Prestação de serviço de transporte para execução de serviço da mesma natureza"),
        ("353", "Prestação de serviço de transporte a estabelecimento comercial"),
        ("357", "Prestação de serviço de transporte a não contribuinte"),
        ("401", "Venda de produção do estabelecimento em operação com produto sujeito ao regime de substituição tributária, na condição de contribuinte substituto"),
        ("402", "Venda de produção do estabelecimento de produto sujeito ao regime de substituição tributária, em operação entre contribuintes substitutos do mesmo produto"),
        ("403", "Venda de mercadoria adquirida ou recebida de terceiros em operação com mercadoria sujeita ao regime de substituição tributária, na condição de contribuinte substituto"),
        ("405", "Venda de mercadoria adquirida ou recebida de terceiros em operação com mercadoria sujeita ao regime de substituição tributária, na condição de contribuinte substituído"),
        ("408", "Transferência de produção do estabelecimento em operação com produto sujeito ao regime de substituição tributária"),
        ("409", "Transferência de mercadoria adquirida ou recebida de terceiros em operação com mercadoria sujeita ao regime de substituição tributária"),
        ("410", "Devolução de compra para industrialização ou produção rural em operação com mercadoria sujeita ao regime de substituição tributária"),
        ("411", "Devolução de compra para comercialização em operação com mercadoria sujeita ao regime de substituição tributária"),
        ("412", "Devolução de bem do ativo imobilizado, em operação com mercadoria sujeita ao regime de substituição tributária"),
        ("413", "Devolução de mercadoria destinada ao uso ou consumo, em operação com mercadoria sujeita ao regime de substituição tributária"),
        ("414", "Remessa de produção do estabelecimento para venda fora do estabelecimento em operação com produto sujeito ao regime de substituição tributária"),
        ("415", "Remessa de mercadoria adquirida ou recebida de terceiros para venda fora do estabelecimento, em operação com mercadoria sujeita ao regime de substituição tributária"),
        ("451", "Remessa de animal e de insumo para estabelecimento produtor"),
        ("501", "Remessa de produção do estabelecimento, com fim específico de exportação"),
        ("502", "Remessa de mercadoria adquirida ou recebida de terceiros, com fim específico de exportação"),
        ("503", "Devolução de mercadoria recebida com fim específico de exportação"),
        ("504", "Remessa de mercadoria para formação de lote de exportação, de produtos industrializados ou produzidos pelo próprio estabelecimento"),
        ("505", "Remessa de mercadoria, adquirida ou recebida de terceiros, para formação de lote de exportação"),
        ("551", "Venda de bem do ativo imobilizado"),
        ("552", "Transferência de bem do ativo imobilizado"),
        ("553", "Devolução de compra de bem para o ativo imobilizado"),
        ("554", "Remessa de bem do ativo imobilizado para uso fora do estabelecimento"),
        ("555", "Devolução de bem do ativo imobilizado de terceiro, recebido para uso no estabelecimento"),
        ("556", "Devolução de compra de material de uso ou consumo"),
        ("557", "Transferência de material de uso ou consumo"),
        ("651", "Venda de combustível ou lubrificante de produção do estabelecimento destinado à industrialização subsequente"),
        ("652", "Venda de combustível ou lubrificante de produção do estabelecimento destinado à comercialização"),
        ("653", "Venda de combustível ou lubrificante de produção do estabelecimento destinado a consumidor ou usuário final"),
        ("654", "Venda de combustível ou lubrificante adquirido ou recebido de terceiros destinado à industrialização subsequente"),
        ("655", "Venda de combustível ou lubrificante adquirido ou recebido de terceiros destinado à comercialização"),
        ("656", "Venda de combustível ou lubrificante adquirido ou recebido de terceiros destinado a consumidor ou usuário final"),
        ("657", "Remessa de combustível ou lubrificante adquirido ou recebido de terceiros para venda fora do estabelecimento"),
        ("658", "Transferência de combustível ou lubrificante de produção do estabelecimento"),
        ("659", "Transferência de combustível ou lubrificante adquirido ou recebido de terceiro"),
        ("660", "Devolução de compra de combustível ou lubrificante adquirido para industrialização subsequente"),
        ("661", "Devolução de compra de combustível ou lubrificante adquirido para comercialização"),
        ("662", "Devolução de compra de combustível ou lubrificante adquirido por consumidor ou usuário final"),
        ("663", "Remessa para armazenagem de combustível ou lubrificante"),
        ("664", "Retorno de combustível ou lubrificante recebido para armazenagem"),
        ("901", "Remessa para industrialização por encomenda"),
        ("902", "Retorno de mercadoria utilizada na industrialização por encomenda"),
        ("903", "Retorno de mercadoria recebida para industrialização e não aplicada no referido processo"),
        ("904", "Remessa para venda fora do estabelecimento"),
        ("905", "Remessa para depósito fechado ou armazém geral"),
        ("906", "Retorno de mercadoria depositada em depósito fechado ou armazém geral"),
        ("907", "Retorno simbólico de mercadoria depositada em depósito fechado ou armazém geral"),
        ("908", "Remessa de bem por conta de contrato de comodato"),
        ("909", "Retorno de bem recebido por conta de contrato de comodato"),
        ("910", "Remessa em bonificação, doação ou brinde"),
        ("911", "Remessa de amostra grátis"),
        ("912", "Remessa de mercadoria ou bem para demonstração, mostruário ou treinamento"),
        ("913", "Retorno de mercadoria ou bem recebido para demonstração ou mostruário"),
        ("914", "Remessa de mercadoria ou bem para exposição ou feira"),
        ("915", "Remessa de mercadoria ou bem para conserto ou reparo"),
        ("916", "Retorno de mercadoria ou bem recebido para conserto ou reparo"),
        ("917", "Remessa de mercadoria em consignação mercantil ou industrial"),
        ("918", "Devolução de mercadoria recebida em consignação mercantil ou industrial"),
        ("919", "Devolução simbólica de mercadoria vendida ou utilizada em processo industrial, recebida anteriormente em consignação mercantil ou industrial"),
        ("920", "Remessa de vasilhame ou sacaria"),
        ("921", "Devolução de vasilhame ou sacaria"),
        ("922", "Lançamento efetuado a título de simples faturamento decorrente de venda para entrega futura"),
        ("923", "Remessa de mercadoria por conta e ordem de terceiros, em venda à ordem"),
        ("924", "Remessa para industrialização por conta e ordem do adquirente da mercadoria, quando esta não transitar pelo estabelecimento do adquirente"),
        ("925", "Retorno de mercadoria recebida para industrialização por conta e ordem do adquirente da mercadoria, quando aquela não transitar pelo estabelecimento do adquirente"),
        ("929", "Lançamento efetuado em decorrência de emissão de documento fiscal relativo a operação ou prestação também registrada em equipamento Emissor de Cupom Fiscal - ECF"),
        ("949", "Outra saída de mercadoria ou prestação de serviço não especificada"),
    ];

    /// <summary>Só existem dentro do estado (não têm espelho 2/6).</summary>
    private static readonly HashSet<string> InStateOnly = [];

    private static readonly FiscalCode[] Extra =
    [
        new("1601", "Recebimento, por transferência, de crédito de ICMS"),
        new("1602", "Recebimento, por transferência, de saldo credor de ICMS de outro estabelecimento da mesma empresa, para compensação de saldo devedor de ICMS"),
        new("1603", "Ressarcimento de ICMS retido por substituição tributária"),
        new("1604", "Lançamento do crédito relativo à compra de bem para o ativo imobilizado"),
        new("1605", "Recebimento, por transferência, de saldo devedor de ICMS de outro estabelecimento da mesma empresa"),
        new("2603", "Ressarcimento de ICMS retido por substituição tributária"),
        new("5601", "Transferência de crédito de ICMS acumulado"),
        new("5602", "Transferência de saldo credor de ICMS para outro estabelecimento da mesma empresa, destinado à compensação de saldo devedor de ICMS"),
        new("5603", "Ressarcimento de ICMS retido por substituição tributária"),
        new("5605", "Transferência de saldo devedor de ICMS de outro estabelecimento da mesma empresa"),
        new("5606", "Utilização de saldo credor de ICMS para extinção por compensação de débitos fiscais"),
        new("6107", "Venda de produção do estabelecimento, destinada a não contribuinte"),
        new("6108", "Venda de mercadoria adquirida ou recebida de terceiros, destinada a não contribuinte"),
        new("6603", "Ressarcimento de ICMS retido por substituição tributária"),
        new("3101", "Compra para industrialização (importação)"),
        new("3102", "Compra para comercialização (importação)"),
        new("3201", "Devolução de venda de produção do estabelecimento (exportação)"),
        new("3202", "Devolução de venda de mercadoria adquirida ou recebida de terceiros (exportação)"),
        new("3551", "Compra de bem para o ativo imobilizado (importação)"),
        new("3556", "Compra de material para uso ou consumo (importação)"),
        new("3949", "Outra entrada de mercadoria ou prestação de serviço não especificada (exterior)"),
        new("7101", "Venda de produção do estabelecimento (exportação)"),
        new("7102", "Venda de mercadoria adquirida ou recebida de terceiros (exportação)"),
        new("7201", "Devolução de compra para industrialização (importação)"),
        new("7202", "Devolução de compra para comercialização (importação)"),
        new("7501", "Exportação de mercadorias recebidas com fim específico de exportação"),
        new("7551", "Venda de bem do ativo imobilizado (exportação)"),
        new("7949", "Outra saída de mercadoria ou prestação de serviço não especificada (exterior)"),
    ];

    public static readonly IReadOnlyList<FiscalCode> Cfop = BuildCfop();

    private static readonly HashSet<string> CfopCodes = Cfop.Select(item => item.Code).ToHashSet();

    public static readonly IReadOnlyList<FiscalCode> IcmsCst =
    [
        new("00", "Tributada integralmente"),
        new("02", "Tributação monofásica própria sobre combustíveis"),
        new("10", "Tributada e com cobrança do ICMS por substituição tributária"),
        new("15", "Tributação monofásica própria e com responsabilidade pela retenção sobre combustíveis"),
        new("20", "Com redução de base de cálculo"),
        new("30", "Isenta ou não tributada e com cobrança do ICMS por substituição tributária"),
        new("40", "Isenta"),
        new("41", "Não tributada"),
        new("50", "Suspensão"),
        new("51", "Diferimento"),
        new("53", "Tributação monofásica sobre combustíveis com recolhimento diferido"),
        new("60", "ICMS cobrado anteriormente por substituição tributária"),
        new("61", "Tributação monofásica sobre combustíveis cobrada anteriormente"),
        new("70", "Com redução de base de cálculo e cobrança do ICMS por substituição tributária"),
        new("90", "Outras"),
    ];

    public static readonly IReadOnlyList<FiscalCode> Csosn =
    [
        new("101", "Tributada pelo Simples Nacional com permissão de crédito"),
        new("102", "Tributada pelo Simples Nacional sem permissão de crédito"),
        new("103", "Isenção do ICMS no Simples Nacional para faixa de receita bruta"),
        new("201", "Tributada pelo Simples Nacional com permissão de crédito e com cobrança do ICMS por substituição tributária"),
        new("202", "Tributada pelo Simples Nacional sem permissão de crédito e com cobrança do ICMS por substituição tributária"),
        new("203", "Isenção do ICMS no Simples Nacional para faixa de receita bruta e com cobrança do ICMS por substituição tributária"),
        new("300", "Imune"),
        new("400", "Não tributada pelo Simples Nacional"),
        new("500", "ICMS cobrado anteriormente por substituição tributária (substituído) ou por antecipação"),
        new("900", "Outros"),
    ];

    public static readonly IReadOnlyList<FiscalCode> PisCofinsCst =
    [
        new("01", "Operação tributável com alíquota básica"),
        new("02", "Operação tributável com alíquota diferenciada"),
        new("03", "Operação tributável com alíquota por unidade de medida de produto"),
        new("04", "Operação tributável monofásica - revenda a alíquota zero"),
        new("05", "Operação tributável por substituição tributária"),
        new("06", "Operação tributável a alíquota zero"),
        new("07", "Operação isenta da contribuição"),
        new("08", "Operação sem incidência da contribuição"),
        new("09", "Operação com suspensão da contribuição"),
        new("49", "Outras operações de saída"),
        new("50", "Operação com direito a crédito - vinculada exclusivamente a receita tributada no mercado interno"),
        new("51", "Operação com direito a crédito - vinculada exclusivamente a receita não tributada no mercado interno"),
        new("52", "Operação com direito a crédito - vinculada exclusivamente a receita de exportação"),
        new("53", "Operação com direito a crédito - vinculada a receitas tributadas e não tributadas no mercado interno"),
        new("54", "Operação com direito a crédito - vinculada a receitas tributadas no mercado interno e de exportação"),
        new("55", "Operação com direito a crédito - vinculada a receitas não tributadas no mercado interno e de exportação"),
        new("56", "Operação com direito a crédito - vinculada a receitas tributadas e não tributadas no mercado interno e de exportação"),
        new("60", "Crédito presumido - operação de aquisição vinculada exclusivamente a receita tributada no mercado interno"),
        new("61", "Crédito presumido - operação de aquisição vinculada exclusivamente a receita não tributada no mercado interno"),
        new("62", "Crédito presumido - operação de aquisição vinculada exclusivamente a receita de exportação"),
        new("63", "Crédito presumido - operação de aquisição vinculada a receitas tributadas e não tributadas no mercado interno"),
        new("64", "Crédito presumido - operação de aquisição vinculada a receitas tributadas no mercado interno e de exportação"),
        new("65", "Crédito presumido - operação de aquisição vinculada a receitas não tributadas no mercado interno e de exportação"),
        new("66", "Crédito presumido - operação de aquisição vinculada a receitas tributadas e não tributadas no mercado interno e de exportação"),
        new("67", "Crédito presumido - outras operações"),
        new("70", "Operação de aquisição sem direito a crédito"),
        new("71", "Operação de aquisição com isenção"),
        new("72", "Operação de aquisição com suspensão"),
        new("73", "Operação de aquisição a alíquota zero"),
        new("74", "Operação de aquisição sem incidência da contribuição"),
        new("75", "Operação de aquisição por substituição tributária"),
        new("98", "Outras operações de entrada"),
        new("99", "Outras operações"),
    ];

    public static readonly IReadOnlyList<FiscalCode> IpiCst =
    [
        new("00", "Entrada com recuperação de crédito"),
        new("01", "Entrada tributada com alíquota zero"),
        new("02", "Entrada isenta"),
        new("03", "Entrada não tributada"),
        new("04", "Entrada imune"),
        new("05", "Entrada com suspensão"),
        new("49", "Outras entradas"),
        new("50", "Saída tributada"),
        new("51", "Saída tributada com alíquota zero"),
        new("52", "Saída isenta"),
        new("53", "Saída não tributada"),
        new("54", "Saída imune"),
        new("55", "Saída com suspensão"),
        new("99", "Outras saídas"),
    ];

    public static readonly IReadOnlyList<FiscalCode> Origin =
    [
        new("0", "Nacional, exceto as indicadas nos códigos 3, 4, 5 e 8"),
        new("1", "Estrangeira - importação direta, exceto a indicada no código 6"),
        new("2", "Estrangeira - adquirida no mercado interno, exceto a indicada no código 7"),
        new("3", "Nacional, com conteúdo de importação superior a 40% e inferior ou igual a 70%"),
        new("4", "Nacional, produzida conforme processos produtivos básicos"),
        new("5", "Nacional, com conteúdo de importação inferior ou igual a 40%"),
        new("6", "Estrangeira - importação direta, sem similar nacional, constante em lista CAMEX"),
        new("7", "Estrangeira - adquirida no mercado interno, sem similar nacional, constante em lista CAMEX"),
        new("8", "Nacional, com conteúdo de importação superior a 70%"),
    ];

    public static FiscalTablesDto All() => new(Cfop, IcmsCst, Csosn, PisCofinsCst, IpiCst, Origin);

    public static bool IsCfop(string? code) => code is not null && CfopCodes.Contains(code);

    public static string? CfopDescription(string? code) => Cfop.FirstOrDefault(item => item.Code == code)?.Description;

    private static List<FiscalCode> BuildCfop()
    {
        var list = new List<FiscalCode>();

        void Group(char inState, char otherState, (string Code, string Description)[] rows, string suffixOther)
        {
            foreach (var (code, description) in rows)
            {
                list.Add(new FiscalCode($"{inState}{code}", description));

                if (!InStateOnly.Contains($"{inState}{code}"))
                {
                    list.Add(new FiscalCode($"{otherState}{code}", $"{description} {suffixOther}"));
                }
            }
        }

        Group('1', '2', Inbound, "(de outro estado)");
        Group('5', '6', Outbound, "(para outro estado)");
        list.AddRange(Extra);
        return list.DistinctBy(item => item.Code).OrderBy(item => item.Code, StringComparer.Ordinal).ToList();
    }
}
