using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using DFe.Utils;
using Microsoft.Extensions.Logging;
using NFe.Classes;
using NFe.Classes.Informacoes;
using NFe.Classes.Informacoes.Destinatario;
using NFe.Classes.Informacoes.Detalhe;
using NFe.Classes.Informacoes.Detalhe.Tributacao;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Estadual;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Estadual.Tipos;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Federal;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Federal.Tipos;
using NFe.Classes.Informacoes.Emitente;
using NFe.Classes.Informacoes.Identificacao;
using NFe.Classes.Informacoes.Identificacao.Tipos;
using NFe.Classes.Informacoes.Observacoes;
using NFe.Classes.Informacoes.Pagamento;
using NFe.Classes.Informacoes.Total;
using NFe.Classes.Informacoes.Transporte;
using NFe.Classes.Servicos.Tipos;
using NFe.Servicos;
using NFe.Utils;
using NFe.Utils.Excecoes;
using NFe.Utils.InformacoesSuplementares;
using NFe.Utils.NFe;
using ToNoRango.Application.Fiscal;
using NFeDocument = NFe.Classes.NFe;

namespace ToNoRango.Infrastructure.Fiscal;

/// <summary>
/// NF-e (55) and NFC-e (65) straight to the SEFAZ with the store's own A1 certificate, through the
/// free Zeus/Hercules library. Each call builds its own configuration, so stores never share state.
/// Built for Lucro Presumido and Real (CRT 3), the only regimes the fiscal module serves.
/// </summary>
public sealed partial class ZeusDfeEmitter(ILogger<ZeusDfeEmitter> logger) : IDfeEmitter
{
    public const string HomologationDescription = "NOTA FISCAL EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";

    /// <summary>Recipient name the SEFAZ requires in homologation (fits the 60-character field).</summary>
    public const string HomologationRecipient = "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";

    private const int TimeoutMs = 30000;

    /// <summary>Official schemas shipped with the API; every note is checked against them before it leaves.</summary>
    public static string SchemasFolder { get; set; } = Path.Combine(AppContext.BaseDirectory, "Fiscal", "Schemas");

    public DfeResult Sign(DfeIssue issue)
    {
        var config = Configure(issue.Model, issue.Environment, issue.Issuer.State, issue.OfflineContingency ? TipoEmissao.teOffLine : TipoEmissao.teNormal, issue.Credentials);
        using var certificate = Load(issue.Credentials);
        var note = Build(issue, config, certificate);
        return new DfeResult(DfeOutcome.SignedOffline, "Nota assinada e validada.", AccessKey(note), null, note.ObterXmlString());
    }

    public Task<DfeResult> AuthorizeAsync(DfeIssue issue, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var config = Configure(issue.Model, issue.Environment, issue.Issuer.State, TipoEmissao.teNormal, issue.Credentials);
        using var certificate = Load(issue.Credentials);
        NFeDocument note;

        try
        {
            note = Build(issue, config, certificate);
        }
        catch (ValidacaoSchemaException exception)
        {
            return new DfeResult(DfeOutcome.Rejected, $"A nota não passou na validação: {exception.Message}");
        }

        return Send(note, config, certificate);
    }, cancellationToken);

    public Task<DfeResult> SendSignedAsync(int model, int environment, string state, string signedXml, DfeCredentials credentials, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var config = Configure(model, environment, state, TipoEmissao.teOffLine, credentials);
        using var certificate = Load(credentials);
        var note = new NFeDocument().CarregarDeXmlString(signedXml);
        return Send(note, config, certificate);
    }, cancellationToken);

    public Task<DfeEventResult> CancelAsync(DfeCancel request, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var config = Configure(request.Model, request.Environment, request.State, TipoEmissao.teNormal, request.Credentials);
        using var certificate = Load(request.Credentials);

        try
        {
            using var service = new ServicosNFe(config, certificate);
            var answer = service.RecepcaoEventoCancelamento(1, 1, request.Protocol, request.AccessKey, request.Reason, request.Cnpj, DateTimeOffset.Now);
            var item = answer.Retorno.retEvento?.FirstOrDefault()?.infEvento;
            var status = item?.cStat ?? answer.Retorno.cStat;

            // 135 = event registered; 155 = cancelled after the deadline but accepted.
            return status is 135 or 155
                ? new DfeEventResult(true, item?.xMotivo ?? "Cancelamento registrado.", item?.nProt, answer.RetornoCompletoStr)
                : new DfeEventResult(false, $"{status} - {item?.xMotivo ?? answer.Retorno.xMotivo}");
        }
        catch (Exception exception) when (IsCommunication(exception))
        {
            logger.LogWarning(exception, "SEFAZ did not answer a cancellation");
            return new DfeEventResult(false, $"A SEFAZ não respondeu: {exception.Message}");
        }
    }, cancellationToken);

    public Task<DfeDistribution> DistributeAsync(int environment, string state, string cnpj, long lastNsu, DfeCredentials credentials, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var config = Configure(55, environment, state, TipoEmissao.teNormal, credentials);
        using var certificate = Load(credentials);

        try
        {
            using var service = new ServicosNFe(config, certificate);
            var answer = service.NfeDistDFeInteresse(((int)State(state)).ToString("00"), cnpj, lastNsu.ToString("000000000000000"));
            var result = answer.Retorno;
            var documents = (result.loteDistDFeInt ?? [])
                .Select(item => new DfeDistributedDocument(item.NSU, item.schema ?? string.Empty, Unzip(item.XmlNfe)))
                .ToList();
            return new DfeDistribution(result.cStat, result.xMotivo, result.ultNSU, result.maxNSU, documents);
        }
        catch (Exception exception) when (IsCommunication(exception))
        {
            logger.LogWarning(exception, "SEFAZ did not answer the DF-e distribution");
            return new DfeDistribution(0, $"A SEFAZ não respondeu: {exception.Message}", lastNsu, lastNsu, []);
        }
    }, cancellationToken);

    public Task<DfeEventResult> AcknowledgeAsync(int environment, string state, string cnpj, string accessKey, DfeCredentials credentials, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var config = Configure(55, environment, state, TipoEmissao.teNormal, credentials);
        using var certificate = Load(credentials);

        try
        {
            using var service = new ServicosNFe(config, certificate);
            var answer = service.RecepcaoEventoManifestacaoDestinatario(1, 1, accessKey, NFeTipoEvento.TeMdCienciaDaOperacao, cnpj);
            var item = answer.Retorno.retEvento?.FirstOrDefault()?.infEvento;
            var status = item?.cStat ?? answer.Retorno.cStat;

            // 135 registrado; 573 = já havia ciência para esta nota.
            return status is 135 or 573
                ? new DfeEventResult(true, item?.xMotivo ?? "Ciência registrada.", item?.nProt, answer.RetornoCompletoStr)
                : new DfeEventResult(false, $"{status} - {item?.xMotivo ?? answer.Retorno.xMotivo}");
        }
        catch (Exception exception) when (IsCommunication(exception))
        {
            logger.LogWarning(exception, "SEFAZ did not answer an acknowledgement");
            return new DfeEventResult(false, $"A SEFAZ não respondeu: {exception.Message}");
        }
    }, cancellationToken);

    /// <summary>Each distributed document comes gzipped; tolerates plain bytes too.</summary>
    private static string Unzip(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return string.Empty;
        }

        if (bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
        {
            using var input = new MemoryStream(bytes);
            using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, System.Text.Encoding.UTF8);
            return reader.ReadToEnd();
        }

        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    public Task<DfeEventResult> CorrectAsync(DfeCorrection request, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var config = Configure(55, request.Environment, request.State, TipoEmissao.teNormal, request.Credentials);
        using var certificate = Load(request.Credentials);

        try
        {
            using var service = new ServicosNFe(config, certificate);
            var answer = service.RecepcaoEventoCartaCorrecao(1, request.Sequence, request.AccessKey, request.Text, request.Cnpj, DateTimeOffset.Now);
            var item = answer.Retorno.retEvento?.FirstOrDefault()?.infEvento;
            var status = item?.cStat ?? answer.Retorno.cStat;

            // 135 = event registered and linked to the note.
            return status == 135
                ? new DfeEventResult(true, item?.xMotivo ?? "Carta de correção registrada.", item?.nProt, answer.RetornoCompletoStr)
                : new DfeEventResult(false, $"{status} - {item?.xMotivo ?? answer.Retorno.xMotivo}");
        }
        catch (Exception exception) when (IsCommunication(exception))
        {
            logger.LogWarning(exception, "SEFAZ did not answer a correction letter");
            return new DfeEventResult(false, $"A SEFAZ não respondeu: {exception.Message}");
        }
    }, cancellationToken);

    public Task<DfeEventResult> GiveUpNumbersAsync(DfeNumberGap request, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var config = Configure(request.Model, request.Environment, request.State, TipoEmissao.teNormal, request.Credentials);
        using var certificate = Load(request.Credentials);

        try
        {
            using var service = new ServicosNFe(config, certificate);
            var answer = service.NfeInutilizacao(request.Cnpj, request.Year % 100, Model(request.Model), request.Series, request.FromNumber, request.ToNumber, request.Reason);
            var info = answer.Retorno.infInut;

            // 102 = numbering given up.
            return info.cStat == 102
                ? new DfeEventResult(true, info.xMotivo, info.nProt, answer.RetornoCompletoStr)
                : new DfeEventResult(false, $"{info.cStat} - {info.xMotivo}");
        }
        catch (Exception exception) when (IsCommunication(exception))
        {
            logger.LogWarning(exception, "SEFAZ did not answer an inutilização");
            return new DfeEventResult(false, $"A SEFAZ não respondeu: {exception.Message}");
        }
    }, cancellationToken);

    public Task<DfeEventResult> StatusAsync(int model, int environment, string state, DfeCredentials credentials, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var config = Configure(model, environment, state, TipoEmissao.teNormal, credentials);
        using var certificate = Load(credentials);

        try
        {
            using var service = new ServicosNFe(config, certificate);
            var answer = service.NfeStatusServico();
            return new DfeEventResult(answer.Retorno.cStat == 107, $"{answer.Retorno.cStat} - {answer.Retorno.xMotivo}");
        }
        catch (Exception exception) when (IsCommunication(exception))
        {
            return new DfeEventResult(false, $"A SEFAZ não respondeu: {exception.Message}");
        }
    }, cancellationToken);

    public (string Subject, string? Cnpj, DateTimeOffset ExpiresAt) ReadCertificate(byte[] pfx, string password)
    {
        using var certificate = X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.EphemeralKeySet);

        if (!certificate.HasPrivateKey)
        {
            throw new ArgumentException("O arquivo não tem a chave privada. Exporte o certificado A1 com a chave (arquivo .pfx).");
        }

        // e-CNPJ carries the CNPJ after the company name, as "NOME:00000000000000".
        var match = CnpjInSubject().Match(certificate.Subject);
        return (certificate.Subject, match.Success ? match.Groups[1].Value : null, new DateTimeOffset(certificate.NotAfter.ToUniversalTime()));
    }

    private DfeResult Send(NFeDocument note, ConfiguracaoServico config, X509Certificate2 certificate)
    {
        var key = AccessKey(note);

        try
        {
            using var service = new ServicosNFe(config, certificate);
            var answer = service.NFeAutorizacao(1, IndicadorSincronizacao.Sincrono, [note]);
            var protocol = answer.Retorno.protNFe?.infProt;

            if (protocol is null)
            {
                // 103/105: received but still processing; the caller asks again later.
                return answer.Retorno.cStat is 103 or 105
                    ? new DfeResult(DfeOutcome.Unanswered, $"{answer.Retorno.cStat} - {answer.Retorno.xMotivo}", key)
                    : new DfeResult(DfeOutcome.Rejected, $"{answer.Retorno.cStat} - {answer.Retorno.xMotivo}", key);
            }

            // 100 = authorized; 150 = authorized after the deadline.
            if (protocol.cStat is 100 or 150)
            {
                var processed = new nfeProc { versao = note.infNFe.versao, NFe = note, protNFe = answer.Retorno.protNFe };
                return new DfeResult(DfeOutcome.Authorized, protocol.xMotivo, protocol.chNFe, protocol.nProt, FuncoesXml.ClasseParaXmlString(processed));
            }

            return new DfeResult(DfeOutcome.Rejected, $"{protocol.cStat} - {protocol.xMotivo}", key);
        }
        catch (Exception exception) when (IsCommunication(exception))
        {
            logger.LogWarning(exception, "SEFAZ did not answer note {Key}", key);
            return new DfeResult(DfeOutcome.Unanswered, $"A SEFAZ não respondeu: {exception.Message}", key, null, note.ObterXmlString());
        }
    }

    private static NFeDocument Build(DfeIssue issue, ConfiguracaoServico config, X509Certificate2 certificate)
    {
        var nfce = issue.Model == 65;
        var homologation = issue.Environment == 2;
        var state = State(issue.Issuer.State);
        var issued = issue.IssuedAt;

        var ide = new ide
        {
            cUF = state,
            natOp = Truncate(issue.Nature ?? "VENDA", 60),
            mod = Model(issue.Model),
            serie = issue.Series,
            nNF = issue.Number,
            tpNF = issue.IsEntry ? TipoNFe.tnEntrada : TipoNFe.tnSaida,
            cMunFG = long.Parse(issue.Issuer.CityIbgeCode),
            tpEmis = config.tpEmis,
            tpImp = nfce ? TipoImpressao.tiNFCe : TipoImpressao.tiRetrato,
            cNF = issue.NumericCode.ToString("00000000"),
            tpAmb = config.tpAmb,
            finNFe = issue.Purpose switch { 2 => FinalidadeNFe.fnComplementar, 3 => FinalidadeNFe.fnAjuste, 4 => FinalidadeNFe.fnDevolucao, _ => FinalidadeNFe.fnNormal },
            verProc = "To-No-Rango 1.0",
            idDest = issue.Recipient?.State is { } destination && !nfce && !string.Equals(destination, issue.Issuer.State, StringComparison.OrdinalIgnoreCase)
                ? DestinoOperacao.doInterestadual
                : DestinoOperacao.doInterna,
            dhEmi = issued,
            procEmi = ProcessoEmissao.peAplicativoContribuinte,
            indFinal = issue.FinalConsumer ? ConsumidorFinal.cfConsumidorFinal : ConsumidorFinal.cfNao,
            indPres = issue.Purpose != 1 ? PresencaComprador.pcNao : issue.IsDelivery ? PresencaComprador.pcEntregaDomicilio : PresencaComprador.pcPresencial,
        };

        if (!nfce)
        {
            ide.dhSaiEnt = issued;
        }

        if (issue.ReferencedKeys is { Count: > 0 } keys)
        {
            ide.NFref = keys.Select(key => new NFref { refNFe = key }).ToList();
        }

        if (issue.OfflineContingency)
        {
            ide.dhCont = issue.ContingencyAt ?? issued;
            ide.xJust = Truncate(issue.ContingencyReason ?? "SEM COMUNICACAO COM A SEFAZ NO MOMENTO DA VENDA", 256);
        }

        var info = new infNFe
        {
            versao = "4.00",
            ide = ide,
            emit = new emit
            {
                CNPJ = issue.Issuer.Cnpj,
                xNome = Truncate(issue.Issuer.LegalName, 60),
                xFant = Truncate(issue.Issuer.TradeName ?? issue.Issuer.LegalName, 60),
                IE = issue.Issuer.StateRegistration,
                CRT = CRT.RegimeNormal,
                enderEmit = new enderEmit
                {
                    xLgr = issue.Issuer.Street,
                    nro = issue.Issuer.Number,
                    xBairro = issue.Issuer.District,
                    cMun = long.Parse(issue.Issuer.CityIbgeCode),
                    xMun = issue.Issuer.City,
                    UF = state,
                    CEP = issue.Issuer.PostalCode,
                    cPais = 1058,
                    xPais = "BRASIL",
                },
            },
            dest = Recipient(issue, homologation),
            transp = new transp { modFrete = issue.IsDelivery && !nfce ? ModalidadeFrete.mfContaEmitenteOumfContaRemetente : ModalidadeFrete.mfSemFrete },
        };

        for (var index = 0; index < issue.Lines.Count; index++)
        {
            info.det.Add(Item(index, issue.Lines[index], homologation && index == 0));
        }

        info.total = Totals(info.det);
        info.pag = Payments(issue, info.total.ICMSTot.vNF);

        if (!string.IsNullOrWhiteSpace(issue.AdditionalInfo))
        {
            info.infAdic = new infAdic { infCpl = Truncate(issue.AdditionalInfo, 5000) };
        }

        var note = new NFeDocument { infNFe = info };
        note.Assina(config, certificate);

        if (nfce)
        {
            if (string.IsNullOrWhiteSpace(issue.Credentials.CscId) || string.IsNullOrWhiteSpace(issue.Credentials.CscToken))
            {
                throw new InvalidOperationException("Cadastre o CSC da NFC-e (fornecido pela SEFAZ) antes de emitir.");
            }

            note.infNFeSupl = new infNFeSupl();
            note.infNFeSupl.urlChave = note.infNFeSupl.ObterUrlConsulta(note, VersaoQrCode.QrCodeVersao2);
            note.infNFeSupl.qrCode = note.infNFeSupl.ObterUrlQrCode(note, VersaoQrCode.QrCodeVersao2, issue.Credentials.CscId, issue.Credentials.CscToken);
        }

        note.Valida(config);
        return note;
    }

    private static dest? Recipient(DfeIssue issue, bool homologation)
    {
        var recipient = issue.Recipient;

        if (recipient is null)
        {
            return null;
        }

        var digits = new string(recipient.Document.Where(char.IsAsciiDigit).ToArray());
        var dest = new dest(VersaoServico.Versao400)
        {
            xNome = homologation ? HomologationRecipient : Truncate(recipient.Name, 60),
            indIEDest = string.IsNullOrWhiteSpace(recipient.StateRegistration) ? indIEDest.NaoContribuinte : indIEDest.ContribuinteICMS,
            email = string.IsNullOrWhiteSpace(recipient.Email) ? null : recipient.Email.Trim(),
        };

        if (digits.Length == 14)
        {
            dest.CNPJ = digits;
        }
        else
        {
            dest.CPF = digits;
        }

        if (!string.IsNullOrWhiteSpace(recipient.StateRegistration))
        {
            dest.IE = new string(recipient.StateRegistration.Where(char.IsAsciiDigit).ToArray());
        }

        if (recipient.Street is not null && recipient.CityIbgeCode is not null && recipient.State is not null)
        {
            dest.enderDest = new enderDest
            {
                xLgr = Truncate(recipient.Street, 60),
                nro = Truncate(recipient.Number ?? "S/N", 60),
                xBairro = Truncate(recipient.District ?? "-", 60),
                cMun = long.Parse(recipient.CityIbgeCode),
                xMun = Truncate(recipient.City ?? "-", 60),
                UF = recipient.State.ToUpperInvariant(),
                CEP = recipient.PostalCode is null ? null : new string(recipient.PostalCode.Where(char.IsAsciiDigit).ToArray()),
                cPais = 1058,
                xPais = "BRASIL",
            };
        }

        return dest;
    }

    private static det Item(int index, DfeLine line, bool homologationLabel)
    {
        var total = Money(line.TotalInCents);
        var freight = Money(line.FreightInCents);
        var discount = Money(line.DiscountInCents);
        var taxBase = total + freight - discount;
        var unit = line.Quantity == 0 ? total : Math.Round(total / line.Quantity, 10);

        var product = new prod
        {
            cProd = Truncate(line.Code, 60),
            cEAN = "SEM GTIN",
            xProd = homologationLabel ? HomologationDescription : Truncate(line.Description, 120),
            NCM = line.Ncm,
            CEST = line.Cest,
            CFOP = int.Parse(line.Cfop),
            uCom = "UN",
            qCom = line.Quantity,
            vUnCom = unit,
            vProd = total,
            cEANTrib = "SEM GTIN",
            uTrib = "UN",
            qTrib = line.Quantity,
            vUnTrib = unit,
            vFrete = freight > 0 ? freight : null,
            vDesc = discount > 0 ? discount : null,
            indTot = IndicadorTotal.ValorDoItemCompoeTotalNF,
        };

        ICMSBasico icms = line.IcmsCst switch
        {
            "00" => new ICMS00
            {
                orig = OrigemMercadoria.OmNacional,
                CST = Csticms.Cst00,
                modBC = DeterminacaoBaseIcms.DbiValorOperacao,
                vBC = taxBase,
                pICMS = line.IcmsRate,
                vICMS = Percent(taxBase, line.IcmsRate),
            },
            "40" => new ICMS40 { orig = OrigemMercadoria.OmNacional, CST = Csticms.Cst40 },
            "41" => new ICMS40 { orig = OrigemMercadoria.OmNacional, CST = Csticms.Cst41 },
            "60" => new ICMS60 { orig = OrigemMercadoria.OmNacional, CST = Csticms.Cst60 },
            _ => throw new InvalidOperationException($"CST de ICMS {line.IcmsCst} ainda não é emitido pelo sistema."),
        };

        var pisCst = Enum.Parse<CSTPIS>($"pis{line.PisCofinsCst}");
        var cofinsCst = Enum.Parse<CSTCOFINS>($"cofins{line.PisCofinsCst}");

        return new det
        {
            nItem = index + 1,
            prod = product,
            imposto = new imposto
            {
                ICMS = new ICMS { TipoICMS = icms },
                PIS = new PIS
                {
                    TipoPIS = line.HasPisCofinsValues
                        ? new PISAliq { CST = pisCst, vBC = taxBase, pPIS = line.PisRate, vPIS = Percent(taxBase, line.PisRate) }
                        : new PISNT { CST = pisCst },
                },
                COFINS = new COFINS
                {
                    TipoCOFINS = line.HasPisCofinsValues
                        ? new COFINSAliq { CST = cofinsCst, vBC = taxBase, pCOFINS = line.CofinsRate, vCOFINS = Percent(taxBase, line.CofinsRate) }
                        : new COFINSNT { CST = cofinsCst },
                },
            },
        };
    }

    private static total Totals(List<det> items)
    {
        var totals = new ICMSTot
        {
            vProd = items.Sum(item => item.prod.vProd),
            vFrete = items.Sum(item => item.prod.vFrete ?? 0),
            vDesc = items.Sum(item => item.prod.vDesc ?? 0),
            vICMSDeson = 0,
            vFCP = 0,
            vFCPST = 0,
            vFCPSTRet = 0,
            vIPIDevol = 0,
            vFCPUFDest = 0,
            vICMSUFDest = 0,
            vICMSUFRemet = 0,
        };

        foreach (var item in items)
        {
            if (item.imposto.ICMS.TipoICMS is ICMS00 icms)
            {
                totals.vBC += icms.vBC;
                totals.vICMS += icms.vICMS;
            }

            if (item.imposto.PIS.TipoPIS is PISAliq pis)
            {
                totals.vPIS += pis.vPIS;
            }

            if (item.imposto.COFINS.TipoCOFINS is COFINSAliq cofins)
            {
                totals.vCOFINS += cofins.vCOFINS;
            }
        }

        totals.vNF = totals.vProd - totals.vDesc + totals.vFrete;
        return new total { ICMSTot = totals };
    }

    private static List<pag> Payments(DfeIssue issue, decimal noteTotal)
    {
        var details = issue.Payments
            .Where(payment => payment.AmountInCents > 0)
            .Select(payment => new detPag
            {
                tPag = payment.Kind switch
                {
                    DfePaymentKind.Cash => FormaPagamento.fpDinheiro,
                    DfePaymentKind.CreditCard => FormaPagamento.fpCartaoCredito,
                    DfePaymentKind.DebitCard => FormaPagamento.fpCartaoDebito,
                    DfePaymentKind.Pix => FormaPagamento.fpPagamentoInstantaneoPIXDinamico,
                    _ => FormaPagamento.fpOutro,
                },
                xPag = payment.Kind == DfePaymentKind.Other ? "Outros" : null,
                vPag = Money(payment.AmountInCents),
            })
            .ToList();

        if (details.Count == 0)
        {
            details.Add(new detPag { tPag = FormaPagamento.fpSemPagamento, vPag = 0 });
        }

        var paid = details.Sum(detail => detail.vPag);
        var pag = new pag { detPag = details };

        // Cash above the total means change: the SEFAZ wants it recorded as vTroco.
        if (paid > noteTotal)
        {
            pag.vTroco = paid - noteTotal;
        }

        return [pag];
    }

    private static ConfiguracaoServico Configure(int model, int environment, string state, TipoEmissao emission, DfeCredentials credentials)
    {
        var config = new ConfiguracaoServico
        {
            tpAmb = environment == 1 ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
            tpEmis = emission,
            ModeloDocumento = Model(model),
            cUF = State(state),
            TimeOut = TimeoutMs,
            ProtocoloDeSeguranca = System.Net.SecurityProtocolType.Tls12,
            DiretorioSchemas = SchemasFolder,
            ValidarSchemas = true,
            SalvarXmlServicos = false,
            DefineVersaoServicosAutomaticamente = true,
            VersaoLayout = VersaoServico.Versao400,
        };

        config.Certificado = new ConfiguracaoCertificado
        {
            TipoCertificado = TipoCertificado.A1ByteArray,
            ArrayBytesArquivo = credentials.Pfx,
            Senha = credentials.Password,
            ManterDadosEmCache = false,
            SignatureMethodSignedXml = "http://www.w3.org/2000/09/xmldsig#rsa-sha1",
            DigestMethodReference = "http://www.w3.org/2000/09/xmldsig#sha1",
        };

        return config;
    }

    private static X509Certificate2 Load(DfeCredentials credentials) =>
        X509CertificateLoader.LoadPkcs12(credentials.Pfx, credentials.Password, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);

    private static ModeloDocumento Model(int model) => model == 55 ? ModeloDocumento.NFe : ModeloDocumento.NFCe;

    private static Estado State(string uf) => Enum.Parse<Estado>(uf.Trim().ToUpperInvariant());

    private static string AccessKey(NFeDocument note) => note.infNFe.Id.Replace("NFe", string.Empty, StringComparison.Ordinal);

    private static decimal Money(long cents) => cents / 100m;

    private static decimal Percent(decimal value, decimal rate) => Math.Round(value * rate / 100m, 2, MidpointRounding.AwayFromZero);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static bool IsCommunication(Exception exception) =>
        exception is ComunicacaoException or HttpRequestException or TimeoutException or System.Net.WebException or TaskCanceledException;

    [GeneratedRegex(@":(\d{14})")]
    private static partial Regex CnpjInSubject();
}
