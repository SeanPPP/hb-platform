using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Linkly;
using static Hbpos.Contracts.Linkly.LinklyCloudBackendStatusConstants;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Wpf.Services;

public interface ILinklyBackendTerminalClient
{
    Task<LinklyConnectionTestResult> TestConnectionAsync(
        CardTerminalEnvironment environment,
        CancellationToken cancellationToken = default);

    Task<LinklyConnectionTestResult> TestTransactionStatusAsync(
        CardTerminalEnvironment environment,
        CancellationToken cancellationToken = default);

    Task<PaymentAuthorizationResult> PurchaseAsync(
        decimal amount,
        PosSessionState session,
        CardTerminalSettings settings,
        CancellationToken cancellationToken = default);

    Task<PaymentAuthorizationResult> RefundAsync(
        decimal amount,
        PosSessionState session,
        CardTerminalSettings settings,
        string? originalReference,
        CancellationToken cancellationToken = default);

    Task<LinklyCloudBackendSessionResponse?> GetResumableSessionAsync(
        CardTerminalSettings settings,
        CancellationToken cancellationToken = default);

    Task<LinklyCloudBackendSessionResponse> RecoverSessionAsync(
        CardTerminalSettings settings,
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<LinklyCloudBackendSessionResponse> ResumeSessionUntilFinalAsync(
        CardTerminalSettings settings,
        LinklyCloudBackendSessionResponse activeStatus,
        CancellationToken cancellationToken = default);

    Task<LinklyCloudBackendSessionResponse> GetSessionStatusAsync(
        CardTerminalSettings settings,
        string sessionId,
        CancellationToken cancellationToken = default);

    Task AcknowledgeSessionAsync(
        CardTerminalSettings settings,
        string sessionId,
        CancellationToken cancellationToken = default);
}

public sealed class LinklyBackendTerminalClient(
    HttpClient httpClient,
    ILinklyTerminalDialogService dialogService,
    TimeSpan? pollInterval = null,
    Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
    ILocalizationService? localization = null,
    ILinklyPaymentAttemptContextAccessor? paymentAttemptContextAccessor = null,
    TimeSpan? businessWait = null) : ILinklyBackendTerminalClient
{
    private const string ProcessorName = "ANZ";
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly TimeSpan _pollInterval = pollInterval.GetValueOrDefault(DefaultPollInterval);
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync = delayAsync ?? Task.Delay;
    private readonly TimeSpan _businessWait = businessWait.GetValueOrDefault(LinklyTimeoutPolicy.BusinessWait);

    public async Task<LinklyConnectionTestResult> TestConnectionAsync(
        CardTerminalEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        var relativeUrl = $"api/v1/linkly/cloud-backend/logon-test?environment={Uri.EscapeDataString(environment.ToString())}";
        var url = FormatRequestUrl(relativeUrl);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            Log($"logon test start environment={environment} componentVersion={GetComponentVersion()}");
            LogHttpRequest(
                "logon-test",
                HttpMethod.Post,
                url,
                txnType: null,
                txnRef: null,
                bodyJson: null);
            using var response = await httpClient.PostAsync(relativeUrl, content: null, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();
            LogHttpResponse(
                "logon-test",
                HttpMethod.Post,
                url,
                response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                txnType: null,
                txnRef: null,
                bodyJson: content);
            if (response.IsSuccessStatusCode)
            {
                var result = ReadLogonTestResult(content);
                Log($"logon test completed environment={environment} success={result.Succeeded} responseCode={LogValue(result.ResponseCode)}");
                return new LinklyConnectionTestResult(result.Succeeded, result.Message);
            }

            var message = TryReadLogonTestMessage(content) ??
                string.Format(
                    CultureInfo.InvariantCulture,
                    T("linkly.backend.logonTestHttpFailed", "ANZ Linkly Cloud logon test failed with HTTP {0}."),
                    (int)response.StatusCode);
            Log($"logon test failed environment={environment} http={(int)response.StatusCode}");
            return new LinklyConnectionTestResult(false, message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log($"logon test failed environment={environment} error={ex.GetType().Name}");
            return new LinklyConnectionTestResult(false, T("linkly.backend.communicationFailed", "ANZ Linkly Cloud backend communication failed."));
        }
    }

    public async Task<LinklyConnectionTestResult> TestTransactionStatusAsync(
        CardTerminalEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        var relativeUrl = $"api/v1/linkly/cloud-backend/status-test?environment={Uri.EscapeDataString(environment.ToString())}";
        var url = FormatRequestUrl(relativeUrl);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            LogHttpRequest(
                "transaction-status-test",
                HttpMethod.Post,
                url,
                txnType: null,
                txnRef: null,
                bodyJson: null);
            using var response = await httpClient.PostAsync(relativeUrl, content: null, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();
            LogHttpResponse(
                "transaction-status-test",
                HttpMethod.Post,
                url,
                response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                txnType: null,
                txnRef: ReadStatusTestTxnRef(content),
                bodyJson: content);

            if (!response.IsSuccessStatusCode)
            {
                var message = TryReadStatusTestMessage(content) ??
                    string.Format(
                        CultureInfo.InvariantCulture,
                        T("linkly.backend.statusTestHttpFailed", "ANZ Linkly Cloud transaction status test failed with HTTP {0}."),
                        (int)response.StatusCode);
                return new LinklyConnectionTestResult(false, message);
            }

            var result = ReadStatusTestResult(content);
            return new LinklyConnectionTestResult(
                result.Succeeded,
                result.Message,
                new LinklyStatusTestDetails(
                    result.TransactionReference,
                    result.RequestedAt,
                    result.ResponseCode,
                    result.ResponseText,
                    result.ResponseTxnRef));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log($"transaction status test failed environment={environment} error={ex.GetType().Name}");
            return new LinklyConnectionTestResult(false, T("linkly.backend.communicationFailed", "ANZ Linkly Cloud backend communication failed."));
        }
    }

    public Task<PaymentAuthorizationResult> PurchaseAsync(
        decimal amount,
        PosSessionState session,
        CardTerminalSettings settings,
        CancellationToken cancellationToken = default)
    {
        return RunAsync("P", amount, session, settings, refundReference: null, cancellationToken);
    }

    public async Task<PaymentAuthorizationResult> RefundAsync(
        decimal amount,
        PosSessionState session,
        CardTerminalSettings settings,
        string? originalReference,
        CancellationToken cancellationToken = default)
    {
        var refundReference = TryParseRefundReference(originalReference);
        Log(
            $"refund reference resolved originalReference={LogValue(originalReference)} refundReference={LogValue(refundReference)} " +
            $"hasRefundReference={!string.IsNullOrWhiteSpace(refundReference)}");
        if (string.IsNullOrWhiteSpace(refundReference))
        {
            refundReference = await TryResolveOriginalBackendRefundReferenceAsync(settings, originalReference, cancellationToken);
        }

        return string.IsNullOrWhiteSpace(refundReference)
            ? new PaymentAuthorizationResult(false, null, T("linkly.backend.refundMissingReference", "Linkly Cloud refund requires an original RFN reference."))
            : await RunAsync("R", amount, session, settings, refundReference, cancellationToken);
    }

    public Task<LinklyCloudBackendSessionResponse?> GetResumableSessionAsync(
        CardTerminalSettings settings,
        CancellationToken cancellationToken = default)
    {
        return GetResumableSessionCoreAsync(settings, cancellationToken);
    }

    public Task<LinklyCloudBackendSessionResponse> RecoverSessionAsync(
        CardTerminalSettings settings,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        return RecoverAsync(settings, sessionId, cancellationToken);
    }

    public Task<LinklyCloudBackendSessionResponse> GetSessionStatusAsync(
        CardTerminalSettings settings,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        return GetStatusAsync(settings, sessionId, cancellationToken);
    }

    public async Task AcknowledgeSessionAsync(
        CardTerminalSettings settings,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var relativeUrl = $"api/v1/linkly/cloud-backend/transactions/{Uri.EscapeDataString(sessionId)}/acknowledge";
        var request = new LinklyCloudBackendAcknowledgeRequest(settings.Environment.ToString());
        LogHttpRequest(
            "acknowledge",
            HttpMethod.Post,
            FormatRequestUrl(relativeUrl),
            txnType: null,
            txnRef: null,
            bodyJson: SerializeDebugJson(request));
        using var response = await httpClient.PostAsJsonAsync(
            relativeUrl,
            request,
            JsonOptions,
            cancellationToken);
        _ = await ReadApiResultAsync(
            response,
            "acknowledge",
            HttpMethod.Post,
            FormatRequestUrl(relativeUrl),
            txnType: null,
            stopwatch,
            cancellationToken);
    }

    private async Task<PaymentAuthorizationResult> RunAsync(
        string txnType,
        decimal amount,
        PosSessionState session,
        CardTerminalSettings settings,
        string? refundReference,
        CancellationToken cancellationToken)
    {
        if (amount <= 0m)
        {
            return new PaymentAuthorizationResult(false, null, T("linkly.backend.amountMustBePositive", "Card amount must be greater than zero."));
        }

        var keepDialogOpen = false;
        var transactionSubmitted = false;
        CancellationTokenSource? transactionTimeoutCts = null;
        Log($"transaction request start txnType={txnType} environment={settings.Environment} componentVersion={GetComponentVersion()}");

        try
        {
            using var preSubmitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // 提交交易前的健康检查使用 HTTP 超时，不占用 Linkly 业务等待窗口。
            preSubmitCts.CancelAfter(LinklyTimeoutPolicy.HttpTimeout);
            var fallbackTxnRef = BuildTxnRef(session);
            var readiness = await CheckBackendReadinessAsync(settings, preSubmitCts.Token);
            if (!readiness.IsReady)
            {
                return FallbackAllowed("linkly.backend.unavailable", readiness.Message);
            }

            var activeStatus = await GetActiveSessionAsync(settings, preSubmitCts.Token);
            if (activeStatus is not null)
            {
                // 闂佽楠稿﹢閬嶁€﹂崼婵愬殨閻犺櫣灏ㄩ懓鍨€掑锝呬壕閻?active session 闂傚倷绀侀幖顐﹀疮閸愭祴鏋栨繛鎴炵瀹曟煡鏌嶈閸撴瑩鈥旈崘顔嘉ч柛顐亜濞堫參姊洪崨濞氭垿鎯勯鐐靛祦濞撴埃鍋撳┑顔瑰亾闂佺粯鐟㈤崑鎾翠繆閹绘帩鐓奸柡宀嬬節瀹曟﹢宕熼鈶╁亾瑜版帒鑸瑰鑸靛姈閻撴盯鏌涢弴銊ュ闁诡垰鐗忕槐鎺撴綇閵娧呯暫缂備礁顑呴ˇ顖烆敇婵傜閱囨繝闈涙缁狅絿绱撻崒姘偓椋庣礊閳ь剟鏌涘☉鍗炵仭闁哄棔鍗冲娲川婵犲倻鐟茬紓浣割儐閸ㄥ湱妲愰幘璇茬閻犲洤寮堕ˉ婵嬫⒑鐟欏嫷鍟忛柛鐘愁殜閹箖宕￠悘璇茬秺閹晠骞嬮幇顓炵伌妞ゃ垺鐗曢埢搴ㄥ箣閻愯尙褰撮梻鍌欑閻忔繈顢栭崶褝鑰块柟缁㈠枟閻?
                return await RejectActiveSessionForNewPaymentAsync(activeStatus, cancellationToken);
            }

            var request = new LinklyCloudBackendTransactionRequest(
                settings.Environment.ToString(),
                txnType,
                ToMinorUnits(amount),
                BuildPurchaseAnalysisData(amount, session, refundReference));

            LinklyCloudBackendSessionResponse status;
            try
            {
                transactionTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                // 交易提交后使用完整业务等待窗口，避免过早中断已提交交易。
                transactionTimeoutCts.CancelAfter(_businessWait);
                transactionSubmitted = true;
                status = await StartTransactionAsync(request, transactionTimeoutCts.Token);
                await NotifyPaymentAttemptSessionStartedAsync(status, cancellationToken);
            }
            catch (LinklyBackendHttpException ex) when (ex.HttpStatus == HttpStatusCode.Conflict)
            {
                // 409 婵犵數鍋涢顓熷垔鐎靛摜绀婇柍褜鍓熼弻鏇㈠幢濡も偓閺嗙喓绱掓潏銊ユ诞鐎规洖銈告慨鈧柍琛″亾缂佽鲸鎹囬幃妤呭捶椤撗呭姼閻庢鍠涘▔娑㈡晝?active session闂傚倷鐒︾€笛呯矙閹寸偟闄勯柡鍐ㄥ€荤粻鏂款熆閼搁潧濮堥柛銈呭閺屻倝骞侀幒鎴濆闁诡垳鍠栭弻锝嗘償閿濆棗娈岄梺鍝ュ櫏閸ㄩ亶骞堥妸銉ф殕闁告洦鍋嗛悡鎴︽⒑缂佹◤顏堝疮閸ф鍊堕柛銉墯閻撴盯鏌涘鈧粈浣糕枍瀹ュ鐓冪憸婊堝礈濮橆剛鏆嗛柟闂寸閻掑灚銇勯幒宥嗩樂濞存嚎鍨洪妵鍕棘閹稿寒妫￠悗鍨緲鐎氼厼顭囪箛娑辨晝闁靛鍔栧ú鐔煎蓟?
                activeStatus = await GetActiveSessionAsync(settings, preSubmitCts.Token);
                if (activeStatus is null)
                {
                    var message = T("linkly.backend.activeSessionUnavailable", "Current terminal has an unfinished card transaction, but no recoverable active session was returned. Try again later.");
                    await PresentFinalFailureAsync("backend-active-unavailable", message, cancellationToken);
                    keepDialogOpen = true;
                    return new PaymentAuthorizationResult(false, null, message);
                }

                // 409 闂傚倷绀侀幉锟犳嚌妤ｅ啫瀚夋い鎺戝閸嬪寮堕崼娑樺妞も晝鍏橀弻銊モ攽閸℃瑥顣堕梺閫炲苯澧柛鐔告綑椤?active session 闂傚倸鍊搁崐鎼佸疮閹剁瓔鏁嬬憸鏃堝Υ娓氣偓閺佹劙宕煎☉妤佺潖闂備浇顫夐崕鎶藉疮閸ф鐑藉川椤掕偐鎳撻埞鍐垂椤旂懓浜鹃柡宥庣仜閿濆憘鏃堝川椤旇姤鐝梺璇茬箳閸嬫盯宕ョ€ｎ喖鑸瑰璺虹灱绾捐棄霉閿濆懏璐￠柟鍏呰兌缁辨帡顢氶崨顓犱哗闂佸疇顕х€涒晠濡堕敐澶婄闁挎梻鎳撴禍鎯ь熆閼搁潧濮囩紒鐘冲浮閺屾洝绠涢弴鐑嗏偓灞句繆閹绘帩鐓奸柡灞剧缁犳稓鈧綆浜滄慨搴ㄦ偡濠婂懎顣肩紒瀣笒椤曘儵宕熼姘鳖槹濡炪倖鎸鹃崰搴ㄥ焵椤掑倸浠遍柡?
                return await RejectActiveSessionForNewPaymentAsync(activeStatus, cancellationToken);
            }
            catch (LinklyBackendHttpException ex) when (IsBackendStartRejectedBeforeSession(ex))
            {
                // 闂傚倷绀侀幉锟犳嚌閹灐褰掓倻缁涘鏅滃銈嗗笒鐎氼剟宕ｆ繝鍥х閺夊牆澧藉畝娑㈡煃瑜滈崜姘舵晝閵忕姷鏆﹂柣鎴ｆ鎯熼梺闈涱槶閸庤櫕绂掗銏″€?session 闂傚倷绀侀幉锟犲箰閸濄儳鐭撻柣銏㈩暯閸嬫挸顫濋渚囨￥缂備浇椴哥敮鎺楀煘閹寸偟绡€閹肩补鍓濋澶嬬節濞堝灝鏋ら柟铏崌瀹曟劙鎮烽柇锔藉瘜闂佺鐬奸崑鐐哄疾椤掑倵鍋撻崗澶婁壕闂佸憡鍔栭崕鍐测枔椤栫偞鈷戦柛婵嗗椤忊晝绱掗妸褍甯舵い顐ｇ箞閺佸啴宕掑顒€鎸ら梻浣筋潐閸庢娊顢氶鐑嗘綎濡わ絽鍟悡娑㈡煕椤愶絿绠ユ俊鎻掓贡閹插憡锛愭担鍝勫缂備礁鍊哥粔瑙勪繆閸洖宸濇い鎾跺Т鐢?Linkly闂傚倷鐒︾€笛呯矙閹达附鍤愭い鏍ㄧ缚娴滃綊鏌涘▎蹇ｆШ妞も晝鍏橀弻鏇熷緞閸繂濮庨梺璇查閵堟悂寮婚悢鑲╁祦闁割煈鍠氭禒濂告⒑缁洘娅呴柡鍫墰缁瑦寰勭仦绋夸壕闁挎繂鍊瑰▍鍥ㄣ亜韫囨挸鑸归柍钘夘樀楠炴瑩宕樿閸戝綊鏌ら崹娑欐珖闁逞屽墯椤旀牠宕伴弽顐ｅ床闁圭増婢橀崒銊╂偡濞嗗繐顏柛搴ｅ枛閺岋繝宕堕妸鍥ㄥ哺瀹?
                var message = string.IsNullOrWhiteSpace(ex.Message)
                    ? T("linkly.backend.configIncomplete", "ANZ Linkly Cloud backend configuration is incomplete.")
                    : ex.Message;
                await PresentFinalFailureAsync("backend-start-rejected", message, cancellationToken);
                return FallbackAllowed("linkly.backend.configIncomplete", message);
            }

            using var localCancelCts = CancellationTokenSource.CreateLinkedTokenSource(
                transactionTimeoutCts.Token,
                dialogService.LocalCancelToken);
            status = await PollUntilFinalAsync(settings, status, localCancelCts.Token);
            var result = ToAuthorizationResult(status, amount, fallbackTxnRef, suppressPrintedReceipt: false);
            keepDialogOpen = !result.Approved;
            return result;
        }
        catch (LinklyBackendLocalCancelException)
        {
            var message = T(
                "linkly.backend.cancelledUnknown",
                "Stopped waiting for the ANZ Linkly Cloud backend card result. The transaction may have reached the terminal; recover the previous transaction or confirm the result in Linkly before retrying.");
            return ResultUnknown("linkly.backend.cancelledUnknown", message);
        }
        catch (OperationCanceledException) when (dialogService.LocalCancelToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            var message = T(
                "linkly.backend.cancelledUnknown",
                "Stopped waiting for the ANZ Linkly Cloud backend card result. The transaction may have reached the terminal; recover the previous transaction or confirm the result in Linkly before retrying.");
            return ResultUnknown("linkly.backend.cancelledUnknown", message);
        }
        catch (OperationCanceledException) when (transactionTimeoutCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
        {
            var message = T("linkly.backend.timeout", "ANZ Linkly Cloud transaction timed out.");
            await PresentFinalFailureAsync("backend-timeout", message, cancellationToken);
            keepDialogOpen = true;
            return transactionSubmitted
                ? ResultUnknown("linkly.backend.resultUnknown", BuildResultUnknownMessage(message))
                : FallbackAllowed("linkly.backend.timeout", message);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            Log(
                $"operation-cancelled source={ex.GetType().Name} transactionSubmitted={transactionSubmitted} " +
                $"businessTimeoutCancelled={transactionTimeoutCts?.IsCancellationRequested == true} " +
                $"localCancelRequested={dialogService.LocalCancelToken.IsCancellationRequested} " +
                $"callerCancelled={cancellationToken.IsCancellationRequested}");
            var message = T(
                "linkly.backend.waitCancelled",
                "Waiting for the ANZ Linkly Cloud backend response was cancelled before the transaction result could be confirmed.");
            await PresentFinalFailureAsync("backend-wait-cancelled", message, cancellationToken);
            keepDialogOpen = true;
            // 濠电偞鍨堕幐鎾磻閹剧粯鐓涢柛鎰硶缁辩増鎱ㄥ鍫㈢暤鐎殿喕鍗抽獮姗€宕橀幓鎺擃吋缂傚倸鍊风粈浣烘崲閹达絻浜瑰ù锝呮贡椤╂煡鏌曢崼婵囧櫣闁绘挸鍊块弻娑橆潩椤掍焦宕冲銈呮禋閸撶喖寮鍥︽勃闁绘劦鍓氱€氭娊姊洪棃鈺侇洭濠⒀勵殜瀹?HTTP 闂備礁鎲￠悷锕傛偋濡ゅ啰鐭撻梺鍨儑閳绘洟鏌ｉ弮鍫缂佺姵鍨甸—鍐Χ閸偄鏁界紓浣虹帛瀹€鎼佺嵁閹邦厽濯撮柧蹇氼潐鐏忔繈姊洪崫鍕靛剱缂侇喖绉撮敃銏ゎ敇閵忊€冲壒闂侀潧顦介崹宕囩矆婢舵劖鈷戞い鎺嗗亾闁诲繑绻堝畷銏ゅΧ婢跺鍘掗悗骞垮劚閹峰危婵犳碍鐓欑痪鏉款槺缁嬬粯銇勯幋鐐垫噰濠?
            return transactionSubmitted
                ? ResultUnknown("linkly.backend.resultUnknown", BuildResultUnknownMessage(message))
                : FallbackAllowed("linkly.backend.waitCancelled", message);
        }
        catch (HttpRequestException)
        {
            var message = T("linkly.backend.communicationFailed", "ANZ Linkly Cloud backend communication failed.");
            await PresentFinalFailureAsync("backend-http-error", message, cancellationToken);
            keepDialogOpen = true;
            return transactionSubmitted
                ? ResultUnknown("linkly.backend.resultUnknown", BuildResultUnknownMessage(message))
                : FallbackAllowed("linkly.backend.communicationFailed", message);
        }
        catch (JsonException)
        {
            var message = T("linkly.backend.invalidResponse", "ANZ Linkly Cloud backend returned an invalid response.");
            await PresentFinalFailureAsync("backend-json-error", message, cancellationToken);
            keepDialogOpen = true;
            return transactionSubmitted
                ? ResultUnknown("linkly.backend.resultUnknown", BuildResultUnknownMessage(message))
                : FallbackAllowed("linkly.backend.invalidResponse", message);
        }
        finally
        {
            transactionTimeoutCts?.Dispose();
            // 闂傚倷鑳堕幊鎾绘偤閵娾晛绀夐柡鍥╁枑閸欏繑绻涢幋鐐垫噮妞も晜鐓￠弻鏇㈠醇濠靛浂妫″銈庡亝缁捇寮婚妶鍡欓檮濠㈣泛顦遍惄搴㈢節濞堝灝鏋撻柡鍛Т椤曪綁濡搁埡浣虹暰闂佺粯顨呴悧鍡涙⒒椤栨稐绻嗛柣鎰典簻閳ь剚顨婂顐ゆ嫚瀹割喚鍔烽梺鍝勵槹椤戞瑩宕甸弴銏＄厱闁挎棁顕ч獮妯尖偓瑙勬礀閻栧ジ寮婚妸銉㈡婵炲棙鍨熷Σ鍫ユ⒑闂堟稒澶勯柛銊ョ秺楠炲繗銇愰幒鎳炽劑鏌ㄩ弮鈧崹婵堝垝椤栨粎纾介柛灞剧懅椤︼箓鏌ｅΔ鈧换鎴﹀箞閵娾晛绠瑰ù锝呮憸閻ｈ鲸绻涙潏鍓хМ妞ゃ儲鎸剧划缁樼鐎ｎ偆鍘介梺鎸庣箓濡盯骞婇崨顖滅＜妞ゆ棁顕у畵鍡欌偓娈垮枔閸旀垵鐣锋總绋垮嵆闁绘梻顭堝▓蹇涙⒒娴ｅ憡鍟炵紒瀣浮閳ワ箓宕堕鈧悘铏繆椤栨艾鎮戝┑顖氥偢閺屾洟宕煎┑鍡樻疁闂?
            if (!keepDialogOpen)
            {
                await dialogService.CloseAsync(cancellationToken);
            }
        }
    }

    public Task<LinklyCloudBackendSessionResponse> ResumeSessionUntilFinalAsync(
        CardTerminalSettings settings,
        LinklyCloudBackendSessionResponse activeStatus,
        CancellationToken cancellationToken = default)
    {
        return ResumeActiveSessionAsync(settings, activeStatus, cancellationToken);
    }

    private async Task<LinklyCloudBackendSessionResponse> ResumeActiveSessionAsync(
        CardTerminalSettings settings,
        LinklyCloudBackendSessionResponse activeStatus,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // 恢复上一笔交易也只等待一个业务窗口，超时后必须保留未知结果。
        timeoutCts.CancelAfter(_businessWait);
        var lastStatus = activeStatus;
        try
        {
            var status = await PresentStatusAsync(
                settings,
                activeStatus,
                T("linkly.backend.activeSessionResume", "Current terminal has an unfinished card transaction. Continuing to poll/recover that session."),
                timeoutCts.Token);
            lastStatus = status;
            if (!IsFinal(status))
            {
                status = await RecoverAsync(settings, status.SessionId, timeoutCts.Token);
                lastStatus = status;
            }

            status = await PollUntilFinalAsync(settings, status, timeoutCts.Token);
            return status;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            var detail = string.Format(
                CultureInfo.InvariantCulture,
                "{0} SessionId={1}; TxnRef={2}; Status={3}.",
                T("linkly.backend.recoveryTimeout", "ANZ Linkly Cloud recovery timed out."),
                LogValue(lastStatus.SessionId),
                LogValue(lastStatus.TxnRef),
                LogValue(lastStatus.Status));
            throw new LinklyBackendResultUnknownException(BuildResultUnknownMessage(detail));
        }
    }

    private async Task<PaymentAuthorizationResult> RejectActiveSessionForNewPaymentAsync(
        LinklyCloudBackendSessionResponse activeStatus,
        CancellationToken cancellationToken)
    {
        var message = T(
            "linkly.backend.activeSessionRequiresRecovery",
            "Current terminal already has an unfinished card transaction. Recover the previous transaction or ask a supervisor to confirm Linkly before starting a new payment.");
        Log(
            $"active session rejected for new payment sessionId={activeStatus.SessionId} " +
            $"txnRef={LogValue(activeStatus.TxnRef)} status={activeStatus.Status}");
        await PresentFinalFailureAsync(activeStatus.SessionId, message, cancellationToken);
        return new PaymentAuthorizationResult(
            false,
            null,
            message,
            StatusKey: "linkly.backend.activeSessionRequiresRecovery");
    }

    private PaymentAuthorizationResult FallbackAllowed(string statusKey, string message)
    {
        return new PaymentAuthorizationResult(false, null, message, StatusKey: statusKey, FallbackAllowed: true);
    }

    private PaymentAuthorizationResult ResultUnknown(string statusKey, string message)
    {
        return new PaymentAuthorizationResult(false, null, message, StatusKey: statusKey, ResultUnknown: true);
    }

    private static bool IsBackendStartRejectedBeforeSession(LinklyBackendHttpException ex)
    {
        return ex.HttpStatus is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity;
    }

    private async Task<BackendReadinessResult> CheckBackendReadinessAsync(
        CardTerminalSettings settings,
        CancellationToken cancellationToken)
    {
        var relativeUrl = $"api/v1/linkly/cloud-backend/health?environment={Uri.EscapeDataString(settings.Environment.ToString())}";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            LogHttpRequest(
                "backend health preflight",
                HttpMethod.Get,
                FormatRequestUrl(relativeUrl),
                txnType: null,
                txnRef: null,
                bodyJson: null);
            using var response = await httpClient.GetAsync(relativeUrl, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();
            LogHttpResponse(
                "backend health preflight",
                HttpMethod.Get,
                FormatRequestUrl(relativeUrl),
                response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                txnType: null,
                txnRef: null,
                bodyJson: content);

            if (!response.IsSuccessStatusCode)
            {
                return BackendReadinessResult.NotReady(BuildBackendUnavailableMessage());
            }

            var health = ReadHealthResult(content);
            if (!health.IsReady)
            {
                var details = FormatHealthFailure(health);
                return BackendReadinessResult.NotReady(string.IsNullOrWhiteSpace(details)
                    ? BuildBackendUnavailableMessage()
                    : details);
            }

            return BackendReadinessResult.Ready;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Log($"backend health preflight failed environment={settings.Environment} error={ex.GetType().Name}");
            return BackendReadinessResult.NotReady(BuildBackendUnavailableMessage());
        }
    }

    private string BuildBackendUnavailableMessage()
    {
        return T(
            "linkly.backend.unavailable",
            "ANZ Linkly Cloud backend API is offline. Cloud backend card payment was not started. Check the network or use another payment method.");
    }

    private string BuildResultUnknownMessage(string detail)
    {
        var guidance = T(
            "linkly.backend.resultUnknown",
            "ANZ Linkly Cloud backend transaction result is unknown. Confirm the Linkly transaction status before retrying.");
        return string.IsNullOrWhiteSpace(detail)
            ? guidance
            : $"{detail} {guidance}";
    }

    private async Task<LinklyCloudBackendSessionResponse> PollUntilFinalAsync(
        CardTerminalSettings settings,
        LinklyCloudBackendSessionResponse status,
        CancellationToken cancellationToken)
    {
        status = await PresentStatusAsync(settings, status, message: null, cancellationToken);
        var shouldRefreshImmediately = !IsFinal(status) && !RequiresRecovery(status);
        while (!IsFinal(status))
        {
            if (shouldRefreshImmediately)
            {
                LogStatusSnapshot("poll loop immediate refresh", status);
                shouldRefreshImmediately = false;
            }
            else
            {
                LogStatusSnapshot("poll loop before delay", status);
                await DelayBeforeNextPollAsync(status, cancellationToken);
            }

            status = RequiresRecovery(status)
                ? await RecoverAsync(settings, status.SessionId, cancellationToken)
                : await GetStatusAsync(settings, status.SessionId, cancellationToken);
            status = await PresentStatusAsync(settings, status, message: null, cancellationToken);
        }

        if (string.Equals(status.Status, StatusCompleted, StringComparison.OrdinalIgnoreCase) &&
            !HasReceipt(status))
        {
            for (var attempt = 0; attempt < 3 && !HasReceipt(status); attempt++)
            {
                await DelayAsync(_pollInterval, cancellationToken);

                status = await GetStatusAsync(settings, status.SessionId, cancellationToken);
                status = await PresentStatusAsync(settings, status, message: null, cancellationToken);
            }
        }

        return status;
    }

    private async Task NotifyPaymentAttemptSessionStartedAsync(
        LinklyCloudBackendSessionResponse status,
        CancellationToken cancellationToken)
    {
        var context = paymentAttemptContextAccessor?.Current;
        if (context is null)
        {
            return;
        }

        try
        {
            await context.BindSessionAsync(
                status.SessionId,
                status.TxnRef,
                DateTimeOffset.UtcNow,
                cancellationToken);
            Log(
                $"payment attempt session bound attemptGuid={context.AttemptGuid} " +
                $"sessionId={status.SessionId} txnRef={LogValue(status.TxnRef)} status={status.Status}");
            LinklyJsonLog.Write(
                "CardRecovery",
                "card-recovery",
                "payment-attempt",
                "session-bound",
                sessionId: status.SessionId,
                success: true,
                details: new
                {
                    timestamp = DateTimeOffset.Now,
                    attemptGuid = context.AttemptGuid,
                    sessionId = status.SessionId,
                    txnRef = NormalizeOptional(status.TxnRef),
                    remoteStatus = status.Status,
                    responseCode = status.ResponseCode,
                    responseText = status.ResponseText,
                    environment = status.Environment,
                    storeCode = status.StoreCode,
                    deviceCode = status.DeviceCode
                });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // session 缂傚倸鍊搁崐鐑芥倿閿曞倸鍨傞柣銏犳啞閸嬧晠鏌ｉ幇闈涘缂佸墎鍋ゅ鍫曞醇椤愵澀鍑介悶姘箖缁绘盯骞嬮悙鏉戠缂備礁顦遍崗妯虹暦濡も偓铻ｉ柤濮愬€愰弸鏍ь渻閵堝棙灏甸柛鐘冲姍閹偤鎳為妷锕€寮垮┑鐘茬仛閹搁箖宕氶悧鍫㈢瘈闁逞屽墴椤㈡棃宕ㄩ缁樺攭婵犵數鍋為崹鍓佸垝閻樿缁╁ù鐘差儐閻撶喖鏌曟繛鍨姎妞ゅ繆鏅犻弻娑㈠煛閸屾粍鍒涢悗瑙勬穿缁绘繈宕洪妷鈺佸窛妞ゆ牗绮犲Σ宄扳攽閻愯埖褰х紒鑼舵铻炴俊銈呭暞瀹曟煡鏌嶈閸撴瑩鈥旈崘顔嘉ч柛顐ｇ箓閹偤姊洪崫鍕棏闁稿鎸荤换娑㈠箣閻愭潙纰嶅銈嗗灥濡瑧绮嬮幒妤婃晩闁芥ê顦辩粣鐐烘倵楠炲灝鍔氶柣妤€绻愬嵄婵鍩栭悡娑㈡煕鐏炶鈧洟鐛鈧弻娑㈠Χ閸涱喗宕冲┑鈥冲级閸旀瑩鏁愰悙渚晞闁芥ê顦竟?
            Log(
                $"payment attempt session bind failed attemptGuid={context.AttemptGuid} " +
                $"sessionId={status.SessionId} txnRef={LogValue(status.TxnRef)} error={ex.GetType().Name}");
            LinklyJsonLog.Write(
                "CardRecovery",
                "card-recovery",
                "payment-attempt",
                "session-bind-failed",
                sessionId: status.SessionId,
                success: false,
                reason: ex.GetType().Name,
                details: new
                {
                    timestamp = DateTimeOffset.Now,
                    attemptGuid = context.AttemptGuid,
                    sessionId = status.SessionId,
                    txnRef = NormalizeOptional(status.TxnRef),
                    remoteStatus = status.Status,
                    responseCode = status.ResponseCode,
                    responseText = status.ResponseText,
                    environment = status.Environment,
                    storeCode = status.StoreCode,
                    deviceCode = status.DeviceCode
                });
        }
    }

    private async Task<LinklyCloudBackendSessionResponse> PresentStatusAsync(
        CardTerminalSettings settings,
        LinklyCloudBackendSessionResponse status,
        string? message,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var dialogState = ToDialogState(status, message);
            var updateStopwatch = Stopwatch.StartNew();
            var action = await dialogService.UpdateAsync(dialogState, cancellationToken);
            updateStopwatch.Stop();
            Log(
                $"dialog update completed sessionId={status.SessionId} elapsedMs={updateStopwatch.ElapsedMilliseconds} " +
                $"status={status.Status} display=\"{LogValue(TruncateForLog(dialogState.DisplayText, 80))}\" " +
                $"buttons={(dialogState.DisplayButtons?.Count ?? 0)} message=\"{LogValue(TruncateForLog(message, 80))}\"");
            if (IsFinal(status) || action is null || string.IsNullOrWhiteSpace(action.Key))
            {
                return status;
            }

            if (IsLocalCancelAction(action))
            {
                throw new LinklyBackendLocalCancelException();
            }

            // sendkey 婵犮垺鍎肩划鍓ф喆閿曞倸瑙﹂幖娣妽娴犳﹢鎮烽弴姘鳖槮闁轰焦鐗犻弻宀冪疀閹惧顦ラ柣鐘叉川缁垰銆掗崼鏇炴闁规鍠栫瑧闂佽鍏涚欢銈囨濠靛鐒奸柛顭戝枛鐢娊鏌熺捄鐚村姛缂侇喗鎸剧划鈺咁敍濞嗘垹鎲梺姹囧妼鐎氼厼銆掗崜浣虹＜闁割偁鍨诲▔銏ゆ煛鐎ｎ偆鐭庢い鏇樺€濆畷姘跺Χ閸℃浜ｉ梺鍝勬媼閸ｏ綁鍩€椤掍浇澹橀柕鍥ㄥ灩閹峰綊濡烽敐鍌氫壕?
            try
            {
                status = await SendKeyAsync(settings, status.SessionId, action, cancellationToken);
                LogStatusSnapshot("manual sendkey completed", status);
                message = null;
            }
            catch (HttpRequestException)
            {
                if (IsCancelSendKeyAction(status, action))
                {
                    throw new LinklyBackendLocalCancelException();
                }

                message = T("linkly.backend.sendKeyFailed", "Card terminal action failed. Try again or recover the transaction.");
                try
                {
                    status = await GetStatusAsync(settings, status.SessionId, cancellationToken);
                }
                catch (HttpRequestException)
                {
                    // 闂傚倷鑳剁划顖炩€﹂崼銉ユ槬闁哄稁鍘奸悞鍨亜閹达絾纭舵い锔肩畵閺屾盯鍩￠崒婊勫垱閻庢鍣崜鐔奉嚕閸撲焦鍟戦柕鍫濆濠⑩偓闂備浇宕垫慨鐢稿礉閹达箑纾块柟缁樺俯濞撳鏌涚仦鍓х煂缁炬崘鍋愰幉姝岀疀濞戞瑥浠虹紓鍌欑劍椤洭鍩炲鍡欑瘈闂傚牊绋撴晶娑欑箾閸繄鐏遍柟鍙夋倐閹囧醇閻旈顣插┑鐘媰閸涱厜锝夋倵閻㈤潧甯舵い顐ｇ箓閻ｇ兘宕堕妸銏＄亖闂備礁鎼ˇ閬嶅磿閹版澘绐楁繛鎴欏焺閺佸淇婇妶鍛櫤闁哄拋鍓熼幃姗€鎮欑捄杞版睏闂佽崵鍠愮换鍫ュ蓟閵堝洠鍋撻崷顓炐㈢悮姘節绾版ǚ鍋撻搹顐㈡殘缂備礁顑呴ˇ鐢稿春閳ь剚銇勯幒鍡椾壕闂佸疇顕ч柊锝夊春閸曨垰绀冮柍鍝勵儔閻涙粓姊绘担鑺ャ€冪紒鈧笟鈧幃褎绻濋崟銊ヤ壕闁割煈鍋嗛惌鎺楁煛鐏炶濮傛い銏＄懇閹剝鎯旈敐蹇曞缂傚倸鍊烽懗鍓佲偓姘箻瀹曠喖顢曢妶鍕崳闂傚倷绀侀幖顐︽偋閸愵喖纾婚柟鎯у绾捐棄霉閿濆懏鎲稿褎娲熼弻鏇㈠炊閵娿儳浠奸梺鐟板槻缂嶅﹥淇婇悜鑺ユ櫜闁稿本鐭竟?
                }

                continue;
            }
        }
    }

    private static bool IsLocalCancelAction(LinklyTerminalDialogAction action)
    {
        return string.Equals(action.Key, LinklyTerminalDialogKeys.LocalCancel, StringComparison.Ordinal);
    }

    private static bool IsCancelSendKeyAction(
        LinklyCloudBackendSessionResponse status,
        LinklyTerminalDialogAction action)
    {
        return status.CancelKeyFlag &&
            !status.OKKeyFlag &&
            string.Equals(LinklyTerminalDialogKeys.Normalize(action.Key), LinklyTerminalDialogKeys.OkCancel, StringComparison.Ordinal);
    }

    private Task PresentFinalFailureAsync(
        string sessionId,
        string message,
        CancellationToken cancellationToken)
    {
        return dialogService.UpdateAsync(
            new LinklyTerminalDialogState(
                sessionId,
                StatusFailed,
                message,
                ReceiptText: null,
                ResponseText: message,
                RecoveryCount: 0,
                LastHttpStatus: null,
                Message: null,
                IsInteractive: false,
                IsFinal: true,
                DisplayButtons: []),
            cancellationToken);
    }

    private static LinklyTerminalDialogState ToDialogState(
        LinklyCloudBackendSessionResponse status,
        string? message)
    {
        var isFinal = IsFinal(status);
        var responseText = NormalizeOptional(status.ResponseText);
        // 闂傚倷绀侀幖顐︽偋閸愵喖纾婚柟鎯у绾捐棄霉閿濆懏鎲稿褎鐩弻宥堫檨闁稿繑绋戦—鍐箳閺冨倻鐣舵繝銏ｆ硾閻偐澹曢懖鈺傚枑闊洦绋掗崑銊モ攽閻樺疇澹橀柛娆忥躬閺屾盯骞樺鐐樂缂侇喚鏁诲娲传閸曨偅鏆㈤梺鍛婂姂閸斿酣宕㈤柆宥嗏拺闁告稑顭堟禒婊堟煕閹惧绠氶柕鍥ㄥ姈閹峰懘鎼归崷顓犲姸闂備胶绮懝楣冾敋椤撶姷鐭嗗鑸靛姈閳锋垶銇勯幒鍡椾壕闂佽绻戠换鍫濈暦閵忋倕围濠㈣泛顑呴崜顕€姊洪崫鍕枌濠碘€虫川缁棃宕奸弴鐔哄幍闂佹眹鍨归悘姘跺吹閳ь剟姊?婵犵數濮伴崹娲磿閼测晛鍨濋柛鎾楀嫬鏋傞梺鎸庢煥婢х晫澹曟總鍛婄厵闁诡垎灞芥婵炲瓨绮犻崹鍫曞蓟?
        var displayText = isFinal
            ? responseText ?? NormalizeOptional(status.Status)
            : IsAutoContinueDisplay(status)
                ? "Waiting for card terminal result..."
            : NormalizeOptional(status.DisplayText);

        return new LinklyTerminalDialogState(
            status.SessionId,
            status.Status,
            displayText,
            ReadReceiptText(status),
            responseText,
            status.RecoveryCount,
            status.LastHttpStatus,
            NormalizeOptional(message),
            LinklyTerminalDialogMode.CloudBackendInteractive,
            IsInteractive: !isFinal,
            IsFinal: isFinal,
            DisplayButtons: BuildDisplayButtons(status),
            InputType: NormalizeOptional(status.InputType),
            GraphicCode: NormalizeOptional(status.GraphicCode));
    }

    private static IReadOnlyList<LinklyTerminalDialogButton> BuildDisplayButtons(
        LinklyCloudBackendSessionResponse status)
    {
        if (IsFinal(status))
        {
            return [];
        }

        var buttons = new List<LinklyTerminalDialogButton>();
        if (!HasDisplayNotification(status))
        {
            return buttons;
        }

        if (IsCardTerminalWaitDisplay(status))
        {
            return buttons;
        }

        // Linkly 闂備浇顕у锕傦綖婢跺孩鎳岄梻?REST sendkey 闂?OK 婵?CANCEL 闂傚倸鍊风欢锟犲窗濞戞瑦鍙忛柕鍫濇啒閿濆牜妲炬繛瀛樼矋閹倸鐣烽悢纰辨晜闁搞儮鏅╁Σ?Key=0闂傚倷鐒︾€笛呯矙閹寸偟闄勯柡鍐ㄥ€荤粻鏃堟煛瀹ュ啫濡块柍缁樻閺屽秷顧侀柛鎾寸懇閸┿垺鎯旈妸銉т紜闂佸憡鍔曞鍫曟煥椤撱垺鈷戦柣鎾虫捣閺嬪啫鈹戦鍝勨偓鏇＄亽婵犮垼娉涢惉濂告儗閸℃稑绾ч柣鎰綑椤ュ銇勮箛鎾宠埞闁宠棄顦甸獮娆撳礃瑜忛弳妤呮⒑?
        if (status.OKKeyFlag && status.CancelKeyFlag)
        {
            buttons.Add(new LinklyTerminalDialogButton("linkly.backend.dialog.button.okCancel", LinklyTerminalDialogKeys.OkCancel));
        }
        else if (status.OKKeyFlag)
        {
            buttons.Add(new LinklyTerminalDialogButton("linkly.backend.dialog.button.ok", LinklyTerminalDialogKeys.OkCancel));
        }

        if (status.AcceptYesKeyFlag)
        {
            buttons.Add(new LinklyTerminalDialogButton("linkly.backend.dialog.button.yesApproved", LinklyTerminalDialogKeys.Yes));
        }

        if (status.DeclineNoKeyFlag)
        {
            buttons.Add(new LinklyTerminalDialogButton("linkly.backend.dialog.button.noDeclined", LinklyTerminalDialogKeys.No, IsDestructive: true));
        }

        if (status.AuthoriseKeyFlag)
        {
            // 缂傚倸鍊烽悞锔剧矙閹烘纾块柟鎯版缁犳牠鏌￠崶銉ョ仼缂佺姵濞婇弻娑㈩敃閵堝懏鐎剧紓鍌氱М閸嬫捇姊绘担鍛婅础妞ゎ厼鐗撹棟妞ゆ牜鍋為崑鈺佲攽閸屾粠鐒炬俊?sendkey AUTH=3 闂傚倷绀侀幉锟犳偡閿曞倸鍨傞柛褎顨呴悞鍨亜閹达絾纭剁紒娑樼箳缁辨帗娼忛妸褏鐣虹紓浣割儏椤︻垶顢樻總绋块唶婵犻潧妫楃粻锝夋⒒閸屾瑧璐伴柛瀣у亾闂佺顑嗛幑鍥蓟濞戙垹鍐€闁靛ě鍐炬椒闂備焦鎮堕崝宥呯暆缁嬫鍤曢柡灞诲労閺佸鏌嶈閸撶喖宕洪埀顒併亜閹哄秶鍔嶉柣銊﹀灴閺屽秹濡烽敂鑽ゅ姺闂佺懓鍢查澶嬩繆閸洖绀冮柨婵嗘噸婢?
            buttons.Add(new LinklyTerminalDialogButton("linkly.backend.dialog.button.authoriseSignature", LinklyTerminalDialogKeys.Auth));
        }

        if (!status.OKKeyFlag && status.CancelKeyFlag)
        {
            buttons.Add(CreateCancelButton());
        }

        return buttons;
    }

    private static LinklyTerminalDialogButton CreateCancelButton()
    {
        return new LinklyTerminalDialogButton(
            "linkly.backend.dialog.button.cancel",
            LinklyTerminalDialogKeys.OkCancel,
            IsDestructive: true);
    }

    private static bool HasDisplayNotification(LinklyCloudBackendSessionResponse status)
    {
        return (status.Notifications ?? [])
            .Any(notification => string.Equals(notification.Type, "display", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCardTerminalWaitDisplay(LinklyCloudBackendSessionResponse status)
    {
        var display = NormalizeDisplayPrompt(status.DisplayText);
        if (IsCardTerminalWaitPrompt(display))
        {
            return true;
        }

        return (status.DisplayLines ?? [])
            .Select(NormalizeDisplayPrompt)
            .Any(IsCardTerminalWaitPrompt);
    }

    private static bool IsAutoContinueDisplay(LinklyCloudBackendSessionResponse status)
    {
        var display = NormalizeDisplayPrompt(status.DisplayText);
        if (IsAutoContinuePrompt(display))
        {
            return true;
        }

        return (status.DisplayLines ?? [])
            .Select(NormalizeDisplayPrompt)
            .Any(IsAutoContinuePrompt);
    }

    private static bool IsAutoContinuePrompt(string? value)
    {
        return string.Equals(value, "TAP OK TO CONTINUE", StringComparison.Ordinal);
    }

    private static bool IsCardTerminalWaitPrompt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("SWIPE CARD", StringComparison.Ordinal) ||
            value.Contains("PRESENT CARD", StringComparison.Ordinal) ||
            value.Contains("INSERT CARD", StringComparison.Ordinal) ||
            value.Contains("TAP CARD", StringComparison.Ordinal) ||
            value.Contains("TAP OK TO CONTINUE", StringComparison.Ordinal) ||
            value.Contains("WAITING FOR CARD", StringComparison.Ordinal);
    }

    private static string? NormalizeDisplayPrompt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return string.Join(' ', value.Trim().ToUpperInvariant().Split(
            [' ', '\r', '\n', '\t'],
            StringSplitOptions.RemoveEmptyEntries));
    }

    private async Task<LinklyCloudBackendSessionResponse> StartTransactionAsync(
        LinklyCloudBackendTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        const string relativeUrl = "api/v1/linkly/cloud-backend/transactions";
        LogHttpRequest(
            "start transaction",
            HttpMethod.Post,
            FormatRequestUrl(relativeUrl),
            request.TxnType,
            txnRef: null,
            bodyJson: SerializeDebugJson(request));
        using var response = await httpClient.PostAsJsonAsync(
            relativeUrl,
            request,
            JsonOptions,
            cancellationToken);
        var status = await ReadApiResultAsync(
            response,
            "start transaction",
            HttpMethod.Post,
            FormatRequestUrl(relativeUrl),
            request.TxnType,
            stopwatch,
            cancellationToken);
        return status;
    }

    private async Task<LinklyCloudBackendSessionResponse?> GetActiveSessionAsync(
        CardTerminalSettings settings,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var relativeUrl = $"api/v1/linkly/cloud-backend/transactions/active?environment={Uri.EscapeDataString(settings.Environment.ToString())}";
        LogHttpRequest(
            "active session",
            HttpMethod.Get,
            FormatRequestUrl(relativeUrl),
            txnType: null,
            txnRef: null,
            bodyJson: null);
        using var response = await httpClient.GetAsync(
            relativeUrl,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();
            LogHttpResponse(
                "active session",
                HttpMethod.Get,
                FormatRequestUrl(relativeUrl),
                response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                txnType: null,
                txnRef: null,
                body);
            return null;
        }

        var status = await ReadApiResultAsync(
            response,
            "active session",
            HttpMethod.Get,
            FormatRequestUrl(relativeUrl),
            txnType: null,
            stopwatch,
            cancellationToken);
        return status;
    }

    private async Task<LinklyCloudBackendSessionResponse?> GetResumableSessionCoreAsync(
        CardTerminalSettings settings,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var relativeUrl = $"api/v1/linkly/cloud-backend/transactions/resumable?environment={Uri.EscapeDataString(settings.Environment.ToString())}";
        LogHttpRequest(
            "resumable session",
            HttpMethod.Get,
            FormatRequestUrl(relativeUrl),
            txnType: null,
            txnRef: null,
            bodyJson: null);
        using var response = await httpClient.GetAsync(
            relativeUrl,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();
            LogHttpResponse(
                "resumable session",
                HttpMethod.Get,
                FormatRequestUrl(relativeUrl),
                response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                txnType: null,
                txnRef: null,
                body);
            return null;
        }

        var status = await ReadApiResultAsync(
            response,
            "resumable session",
            HttpMethod.Get,
            FormatRequestUrl(relativeUrl),
            txnType: null,
            stopwatch,
            cancellationToken);
        return status;
    }

    private async Task<LinklyCloudBackendSessionResponse> GetStatusAsync(
        CardTerminalSettings settings,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var relativeUrl = $"api/v1/linkly/cloud-backend/transactions/{Uri.EscapeDataString(sessionId)}/status?environment={Uri.EscapeDataString(settings.Environment.ToString())}";
        LogHttpRequest(
            "status",
            HttpMethod.Get,
            FormatRequestUrl(relativeUrl),
            txnType: null,
            txnRef: null,
            bodyJson: null);
        using var response = await httpClient.GetAsync(
            relativeUrl,
            cancellationToken);
        var status = await ReadApiResultAsync(
            response,
            "status",
            HttpMethod.Get,
            FormatRequestUrl(relativeUrl),
            txnType: null,
            stopwatch,
            cancellationToken);
        return status;
    }

    private async Task<LinklyCloudBackendSessionResponse> RecoverAsync(
        CardTerminalSettings settings,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var relativeUrl = $"api/v1/linkly/cloud-backend/transactions/{Uri.EscapeDataString(sessionId)}/recover";
        var request = new LinklyCloudBackendRecoverRequest(settings.Environment.ToString());
        LogHttpRequest(
            "recover",
            HttpMethod.Post,
            FormatRequestUrl(relativeUrl),
            txnType: null,
            txnRef: null,
            bodyJson: SerializeDebugJson(request));
        using var response = await httpClient.PostAsJsonAsync(
            relativeUrl,
            request,
            JsonOptions,
            cancellationToken);
        var status = await ReadApiResultAsync(
            response,
            "recover",
            HttpMethod.Post,
            FormatRequestUrl(relativeUrl),
            txnType: null,
            stopwatch,
            cancellationToken);
        return status;
    }

    private async Task<LinklyCloudBackendSessionResponse> SendKeyAsync(
        CardTerminalSettings settings,
        string sessionId,
        LinklyTerminalDialogAction action,
        CancellationToken cancellationToken)
    {
        var normalizedKey = LinklyTerminalDialogKeys.Normalize(action.Key);
        var stopwatch = Stopwatch.StartNew();
        var relativeUrl = $"api/v1/linkly/cloud-backend/transactions/{Uri.EscapeDataString(sessionId)}/sendkey";
        var request = new LinklyCloudBackendSendKeyRequest(
            settings.Environment.ToString(),
            normalizedKey,
            NormalizeOptional(action.Data));
        LogHttpRequest(
            "sendkey",
            HttpMethod.Post,
            FormatRequestUrl(relativeUrl),
            txnType: null,
            txnRef: null,
            bodyJson: SerializeDebugJson(request));
        using var response = await httpClient.PostAsJsonAsync(
            relativeUrl,
            request,
            JsonOptions,
            cancellationToken);
        var status = await ReadApiResultAsync(
            response,
            "sendkey",
            HttpMethod.Post,
            FormatRequestUrl(relativeUrl),
            txnType: null,
            stopwatch,
            cancellationToken);
        return status;
    }

    private async Task<string?> TryResolveOriginalBackendRefundReferenceAsync(
        CardTerminalSettings settings,
        string? originalReference,
        CancellationToken cancellationToken)
    {
        if (!LinklyBackendPaymentReference.TryGetPrintMarker(originalReference, out var environment, out var sessionId) ||
            string.IsNullOrWhiteSpace(sessionId))
        {
            Log($"refund reference recovery skipped reason=no-backend-session originalReference={LogValue(originalReference)}");
            return null;
        }

        if (!string.Equals(environment, settings.Environment.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            Log(
                $"refund reference recovery skipped reason=environment-mismatch referenceEnvironment={LogValue(environment)} " +
                $"settingsEnvironment={settings.Environment}");
            return null;
        }

        try
        {
            var status = await GetStatusAsync(settings, sessionId, cancellationToken);
            var refundReference = TryReadRefundReference(status, originalReference) ??
                TryReadOriginalTxnRef(originalReference);
            Log(
                $"refund reference recovery completed sessionId={sessionId} status={status.Status} " +
                $"notifications={status.Notifications?.Count ?? 0} refundReference={LogValue(refundReference)} " +
                $"transactionPayloads={BuildRefundReferenceRecoverySnapshot(status)}");
            return refundReference;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log($"refund reference recovery failed sessionId={sessionId} error={ex.GetType().Name}");
            return null;
        }
    }

    private static async Task<LinklyCloudBackendSessionResponse> ReadApiResultAsync(
        HttpResponseMessage response,
        string operation,
        HttpMethod method,
        string url,
        string? txnType,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        ApiResult<LinklyCloudBackendSessionResponse>? result = null;
        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                result = JsonSerializer.Deserialize<ApiResult<LinklyCloudBackendSessionResponse>>(content, JsonOptions);
            }
            catch (JsonException) when (!response.IsSuccessStatusCode)
            {
                result = null;
            }
        }
        stopwatch.Stop();
        LogHttpResponse(
            operation,
            method,
            url,
            response.StatusCode,
            stopwatch.ElapsedMilliseconds,
            txnType,
            result?.Data?.TxnRef,
            content);

        if (!response.IsSuccessStatusCode)
        {
            throw new LinklyBackendHttpException(
                result?.Message ?? $"Linkly backend request failed with HTTP {(int)response.StatusCode}.",
                response.StatusCode);
        }

        if (result?.Success != true || result.Data is null)
        {
            throw new LinklyBackendHttpException(
                result?.Message ?? "Linkly backend returned a failure response.",
                response.StatusCode);
        }

        if (string.IsNullOrWhiteSpace(result.Data.SessionId))
        {
            throw new JsonException("Linkly backend response is missing session id.");
        }

        LogStatusSnapshot($"{operation} http response elapsedMs={stopwatch.ElapsedMilliseconds} http={(int)response.StatusCode}", result.Data);
        return result.Data;
    }

    private static string? TryReadApiMessage(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            var result = JsonSerializer.Deserialize<ApiResult<LinklyCloudBackendSessionResponse>>(content, JsonOptions);
            return NormalizeOptional(result?.Message);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LinklyCloudBackendStatusTestResponse ReadStatusTestResult(string content)
    {
        var result = JsonSerializer.Deserialize<ApiResult<LinklyCloudBackendStatusTestResponse>>(content, JsonOptions);
        if (result?.Success != true || result.Data is null)
        {
            throw new LinklyBackendHttpException(
                result?.Message ?? "Linkly backend returned a failure response.",
                HttpStatusCode.OK);
        }

        return result.Data;
    }

    private static LinklyCloudBackendLogonTestResponse ReadLogonTestResult(string content)
    {
        var result = JsonSerializer.Deserialize<ApiResult<LinklyCloudBackendLogonTestResponse>>(content, JsonOptions);
        if (result?.Success != true || result.Data is null)
        {
            throw new LinklyBackendHttpException(
                result?.Message ?? "Linkly backend returned a failure response.",
                HttpStatusCode.OK);
        }

        return result.Data;
    }

    private static string? TryReadLogonTestMessage(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            var result = JsonSerializer.Deserialize<ApiResult<LinklyCloudBackendLogonTestResponse>>(content, JsonOptions);
            return NormalizeOptional(result?.Message ?? result?.Data?.Message);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryReadStatusTestMessage(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            var result = JsonSerializer.Deserialize<ApiResult<LinklyCloudBackendStatusTestResponse>>(content, JsonOptions);
            return NormalizeOptional(result?.Message ?? result?.Data?.Message);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadStatusTestTxnRef(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            var result = JsonSerializer.Deserialize<ApiResult<LinklyCloudBackendStatusTestResponse>>(content, JsonOptions);
            return NormalizeOptional(result?.Data?.ResponseTxnRef);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LinklyCloudBackendHealthResponse ReadHealthResult(string content)
    {
        var result = JsonSerializer.Deserialize<ApiResult<LinklyCloudBackendHealthResponse>>(content, JsonOptions);
        if (result?.Success != true || result.Data is null)
        {
            throw new JsonException("Linkly backend health response is invalid.");
        }

        return result.Data;
    }

    private string FormatHealthFailure(LinklyCloudBackendHealthResponse health)
    {
        var failedMessages = GetFailedHealthChecks(health)
            .Select(check => NormalizeOptional(check.Message))
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return failedMessages.Length == 0
            ? T("linkly.backend.configIncomplete", "ANZ Linkly Cloud backend configuration is incomplete.")
            : string.Join(Environment.NewLine, failedMessages!);
    }

    private static IReadOnlyList<LinklyCloudBackendHealthCheckDto> GetFailedHealthChecks(
        LinklyCloudBackendHealthResponse health)
    {
        return (health.Checks ?? [])
            .Where(check => !check.IsReady)
            .ToArray();
    }

    private PaymentAuthorizationResult ToAuthorizationResult(
        LinklyCloudBackendSessionResponse status,
        decimal requestedAmount,
        string requestedTxnRef,
        bool suppressPrintedReceipt)
    {
        if (string.Equals(status.Status, StatusNotSubmitted, StringComparison.OrdinalIgnoreCase))
        {
            return new PaymentAuthorizationResult(false, null, T("linkly.backend.notSubmitted", "Linkly Cloud transaction was not submitted. Retry the payment."));
        }

        var transactionResult = ReadTransactionResult(status, requestedAmount, requestedTxnRef);
        var amount = transactionResult.Amount ?? requestedAmount;
        var receiptText = ReadReceiptText(status, suppressPrintedReceipt);
        var transaction = ToCardTransaction(transactionResult, amount, receiptText);
        var approved = string.Equals(status.Status, StatusCompleted, StringComparison.OrdinalIgnoreCase) &&
            transactionResult.Succeeded &&
            string.Equals(transactionResult.ResponseCode?.Trim(), "00", StringComparison.OrdinalIgnoreCase);
        var reference = LinklyBackendPaymentReference.Format(
            transaction.TxnRef ?? transactionResult.SessionId,
            transactionResult.SessionId,
            status.Environment,
            transactionResult.RefundReference);

        return approved
            ? new PaymentAuthorizationResult(
                true,
                reference,
                "ANZ Linkly Cloud",
                amount,
                [transaction],
                ProcessorName,
                status.Environment,
                LinklyConnectionMode.CloudBackendAsync.ToString(),
                null,
                status.SessionId,
                transaction.TxnRef,
                transaction.ResponseCode,
                transaction.ResponseText)
            : new PaymentAuthorizationResult(
                false,
                reference,
                FormatResponseMessage(transactionResult.ResponseText, transactionResult.ResponseCode),
                amount,
                [transaction],
                ProcessorName,
                status.Environment,
                LinklyConnectionMode.CloudBackendAsync.ToString(),
                null,
                status.SessionId,
                transaction.TxnRef,
                transaction.ResponseCode,
                transaction.ResponseText);
    }

    private static CardTransactionDto ToCardTransaction(
        LinklyCloudTransactionResult response,
        decimal amount,
        string? receiptText)
    {
        return new CardTransactionDto(
            ProcessorName,
            NormalizeOptional(response.TxnRef) ?? response.SessionId,
            NormalizeOptional(response.AuthCode),
            NormalizeOptional(response.CardType),
            int.TryParse(response.CardName, out var cardName) && cardName > 0 ? cardName : null,
            MaskCardNumber(response.Pan),
            NormalizeOptional(response.Caid),
            NormalizeOptional(response.ResponseCode),
            NormalizeOptional(response.ResponseText),
            NormalizeOptional(response.Stan),
            null,
            decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
            NormalizeOptional(receiptText),
            NormalizeOptional(response.RefundReference));
    }

    private static LinklyCloudTransactionResult ReadTransactionResult(
        LinklyCloudBackendSessionResponse status,
        decimal requestedAmount,
        string requestedTxnRef)
    {
        var protectedResponseCode = NormalizeOptional(status.ResponseCode);
        var protectedResponseText = NormalizeOptional(status.ResponseText);
        var notifications = status.Notifications ?? [];
        var fallbackRefundReference = TryReadRefundReference(status, null);
        var transactionNotification = string.IsNullOrWhiteSpace(protectedResponseCode)
            ? notifications.LastOrDefault(IsTransactionNotification)
            : notifications.LastOrDefault(notification =>
                IsTransactionNotification(notification) &&
                TransactionNotificationMatchesProtectedResult(notification, protectedResponseCode, protectedResponseText));
        if (transactionNotification is null || string.IsNullOrWhiteSpace(transactionNotification.PayloadJson))
        {
            return new LinklyCloudTransactionResult(
                status.SessionId,
                string.Equals(protectedResponseCode, "00", StringComparison.OrdinalIgnoreCase),
                NormalizeOptional(status.TxnRef) ?? requestedTxnRef,
                null,
                null,
                null,
                null,
                null,
                protectedResponseCode,
                protectedResponseText,
                null,
                requestedAmount,
                fallbackRefundReference);
        }

        using var document = JsonDocument.Parse(transactionNotification.PayloadJson);
        var response = ReadResponse(document.RootElement);
        var purchaseAnalysisData = ReadObject(response, "PurchaseAnalysisData");
        var notificationRefundReference = TryReadRefundReference(document.RootElement, out _);
        return new LinklyCloudTransactionResult(
            status.SessionId,
            string.IsNullOrWhiteSpace(protectedResponseCode)
                ? ReadBool(response, "Success") == true
                : string.Equals(protectedResponseCode, "00", StringComparison.OrdinalIgnoreCase),
            NormalizeOptional(status.TxnRef) ?? requestedTxnRef,
            ReadString(response, "AuthCode"),
            ReadString(response, "CardType"),
            ReadString(response, "CardName"),
            ReadString(response, "Pan"),
            ReadString(response, "Caid"),
            protectedResponseCode ?? ReadString(response, "ResponseCode"),
            protectedResponseText ?? ReadString(response, "ResponseText"),
            ReadString(response, "Stan"),
            ReadDecimal(response, "AmtPurchase") ?? requestedAmount,
            ReadString(purchaseAnalysisData, "RFN") ?? notificationRefundReference ?? fallbackRefundReference);
    }

    private static string? TryReadRefundReference(
        LinklyCloudBackendSessionResponse status,
        string? fallbackReference)
    {
        var backendReference = LinklyBackendPaymentReference.TryGetRefundReference(fallbackReference);
        if (!string.IsNullOrWhiteSpace(backendReference))
        {
            return backendReference;
        }

        foreach (var notification in (status.Notifications ?? []).Where(IsTransactionNotification).Reverse())
        {
            if (string.IsNullOrWhiteSpace(notification.PayloadJson))
            {
                continue;
            }

            using var document = JsonDocument.Parse(notification.PayloadJson);
            var refundReference = TryReadRefundReference(document.RootElement, out _);
            if (!string.IsNullOrWhiteSpace(refundReference))
            {
                return refundReference;
            }
        }

        return null;
    }

    private static string BuildRefundReferenceRecoverySnapshot(LinklyCloudBackendSessionResponse status)
    {
        var transactionNotifications = (status.Notifications ?? [])
            .Where(IsTransactionNotification)
            .TakeLast(5)
            .ToArray();
        if (transactionNotifications.Length == 0)
        {
            return "<none>";
        }

        var parts = transactionNotifications.Select((notification, index) =>
        {
            if (string.IsNullOrWhiteSpace(notification.PayloadJson))
            {
                return $"#{index + 1}:empty";
            }

            try
            {
                using var document = JsonDocument.Parse(notification.PayloadJson);
                var root = document.RootElement;
                var response = ReadResponse(root);
                var purchaseAnalysisData = ReadValue(response, "PurchaseAnalysisData");
                var refundReference = TryReadRefundReference(root, out var source);
                return $"#{index + 1}:bytes={notification.PayloadJson.Length},rootKeys={DescribeKeys(root)},responseKeys={DescribeKeys(response)}," +
                    $"padKind={DescribeKind(purchaseAnalysisData)},padKeys={DescribeKeys(purchaseAnalysisData)},rfnSource={LogValue(source)},rfn={LogValue(refundReference)}";
            }
            catch (JsonException ex)
            {
                return $"#{index + 1}:invalid-json:{ex.GetType().Name}";
            }
        });

        return string.Join(" | ", parts);
    }

    private static string? TryReadRefundReference(JsonElement root, out string? source)
    {
        var response = ReadResponse(root);
        var purchaseAnalysisData = ReadValue(response, "PurchaseAnalysisData");
        // Linkly backend 闂?PAD 闂傚倷绶氬鑽ゆ嫻閻旂厧绀夐悗锝庡墰缁犳柨顭块懜闈涘閻熸瑱濡囬埀顒€鍘滈崑鎾绘煕閺囨ê濡煎ù婊呭亾閹便劌螣婢剁鎯堥梺鍛婄憿閸嬫捇姊婚崒娆戝妽鐟滄澘鍟撮幊鐔碱敍濠靛嫪姹楅梺鍝勮閸庢煡宕甸崟顖涚厾闁诡厽甯掗崝婊呯箔閹达附鐓熼幖杈剧稻閺嗏晜銇勯姀鐙呰含闁圭鎳樺畷鐔碱敍濮ｇ鍎遍妴鎺戭潩閿濆懍澹曢梻浣芥〃缁讹繝宕ｉ崘銊ф殾闁圭増婢樼粈鍌炴煕韫囨挸鎮戞い鏂跨Ф缁辨捇宕掑▎鎴М闁诲孩鍑归崣鍐ㄧ暦瑜版帒惟鐟滃宕戦幘鏂ユ婵炲棙蓱閻ｇ厧顪冮妶鍡楃仯闁绘帪濡囩划娆愬緞鐏炴儳鐝伴悷婊冪箳缁牊寰勯幇顓涙嫼濡炪倖鍔楅崰搴㈢閹€鏀介柣鎰级椤ョ娀鏌涚€ｎ偅灏扮紒鍌涘浮椤㈡瑩鎸婃径宀€鐛柣搴＄畭閸庨亶骞婇幇顔句笉闁瑰瓨绻嶅〒濠氭煏閸繄澧遍柛銈嗙懄閵囧嫰寮埀顒€顫忔繝姘疅闁圭虎鍠楅弲鏌ユ煕閳╁啰鎲块柛?RFN闂?
        var refundReference = TryReadRefundReferenceValue(purchaseAnalysisData, "Response.PurchaseAnalysisData", out source);
        if (!string.IsNullOrWhiteSpace(refundReference))
        {
            return refundReference;
        }

        refundReference = TryReadRefundReferenceValue(response, "Response", out source);
        if (!string.IsNullOrWhiteSpace(refundReference))
        {
            return refundReference;
        }

        refundReference = TryReadRefundReferenceValue(root, "Root", out source);
        if (!string.IsNullOrWhiteSpace(refundReference))
        {
            return refundReference;
        }

        source = null;
        return null;
    }

    private static string? TryReadRefundReferenceValue(JsonElement element, string path, out string? source)
    {
        source = null;
        return element.ValueKind switch
        {
            JsonValueKind.Object => TryReadRefundReferenceObject(element, path, out source),
            JsonValueKind.Array => TryReadRefundReferenceArray(element, path, out source),
            JsonValueKind.String => TryReadRefundReferenceFromText(element.GetString(), path, out source),
            JsonValueKind.Number => null,
            JsonValueKind.True => null,
            JsonValueKind.False => null,
            _ => null
        };
    }

    private static string? TryReadRefundReferenceObject(JsonElement element, string path, out string? source)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, "RFN", StringComparison.OrdinalIgnoreCase))
            {
                var value = ReadScalar(property.Value);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    source = $"{path}.{property.Name}";
                    return value;
                }
            }
        }

        var key = ReadString(element, "Key") ??
            ReadString(element, "Name") ??
            ReadString(element, "Tag") ??
            ReadString(element, "Code");
        if (string.Equals(key, "RFN", StringComparison.OrdinalIgnoreCase))
        {
            var value = ReadString(element, "Value") ??
                ReadString(element, "Data") ??
                ReadString(element, "Text");
            if (!string.IsNullOrWhiteSpace(value))
            {
                source = $"{path}[{key}]";
                return value;
            }
        }

        foreach (var property in element.EnumerateObject())
        {
            var value = TryReadRefundReferenceValue(property.Value, $"{path}.{property.Name}", out source);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        source = null;
        return null;
    }

    private static string? TryReadRefundReferenceArray(JsonElement element, string path, out string? source)
    {
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var value = TryReadRefundReferenceValue(item, $"{path}[{index}]", out source);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            index++;
        }

        source = null;
        return null;
    }

    private static string? TryReadRefundReferenceFromText(string? text, string path, out string? source)
    {
        var value = NormalizeOptional(text);
        if (value is null)
        {
            source = null;
            return null;
        }

        var marker = value.IndexOf("RFN", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            source = null;
            return null;
        }

        var start = marker + 3;
        while (start < value.Length && (char.IsWhiteSpace(value[start]) || value[start] is ':' or '=' or '-'))
        {
            start++;
        }

        var end = start;
        while (end < value.Length && !char.IsWhiteSpace(value[end]) && value[end] is not ',' and not ';' and not '|')
        {
            end++;
        }

        var refundReference = NormalizeOptional(value[start..end]);
        source = refundReference is null ? null : path;
        return refundReference;
    }

    private static JsonElement ReadValue(JsonElement root, string propertyName)
    {
        return TryGetProperty(root, propertyName, out var value) ? value : default;
    }

    private static string? ReadScalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => NormalizeOptional(value.GetString()),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static string DescribeKind(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Undefined ? "<missing>" : element.ValueKind.ToString();
    }

    private static string DescribeKeys(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return DescribeKind(element);
        }

        var keys = element.EnumerateObject()
            .Select(property => property.Name)
            .Take(12)
            .ToArray();
        return keys.Length == 0 ? "<empty>" : string.Join(",", keys);
    }

    private static bool IsTransactionNotification(LinklyCloudBackendNotificationDto notification)
    {
        return string.Equals(notification.Type, "transaction", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TransactionNotificationMatchesProtectedResult(
        LinklyCloudBackendNotificationDto notification,
        string protectedResponseCode,
        string? protectedResponseText)
    {
        if (string.IsNullOrWhiteSpace(notification.PayloadJson))
        {
            return false;
        }

        using var document = JsonDocument.Parse(notification.PayloadJson);
        var response = ReadResponse(document.RootElement);
        if (!string.Equals(ReadString(response, "ResponseCode"), protectedResponseCode, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var responseText = ReadString(response, "ResponseText");
        return string.IsNullOrWhiteSpace(protectedResponseText) ||
            string.IsNullOrWhiteSpace(responseText) ||
            string.Equals(responseText, protectedResponseText, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadReceiptText(LinklyCloudBackendSessionResponse status)
    {
        return ReadReceiptText(status, suppressPrintedReceipt: false);
    }

    private static string? ReadReceiptText(
        LinklyCloudBackendSessionResponse status,
        bool suppressPrintedReceipt)
    {
        if (suppressPrintedReceipt && status.ReceiptPrintedAt is not null)
        {
            return null;
        }

        return NormalizeOptional(status.ReceiptText) ?? ReadReceiptText(status.Notifications ?? []);
    }

    private static string? ReadReceiptText(IReadOnlyList<LinklyCloudBackendNotificationDto> notifications)
    {
        var receipts = notifications
            .Where(notification => string.Equals(notification.Type, "receipt", StringComparison.OrdinalIgnoreCase))
            .Select(notification => ReadReceiptNotification(notification.PayloadJson))
            .Where(receipt => !string.IsNullOrWhiteSpace(receipt))
            .Select(receipt => receipt!)
            .ToArray();
        return receipts.Length == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, receipts);
    }

    private static bool HasReceipt(LinklyCloudBackendSessionResponse status)
    {
        return !string.IsNullOrWhiteSpace(status.ReceiptText) ||
            (status.Notifications ?? []).Any(notification =>
                string.Equals(notification.Type, "receipt", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(ReadReceiptNotification(notification.PayloadJson)));
    }

    private static string? ReadReceiptNotification(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(payloadJson);
        return ReadReceiptText(document.RootElement) ?? ReadReceiptText(ReadResponse(document.RootElement));
    }

    private static string? ReadReceiptText(JsonElement element)
    {
        if (!TryGetProperty(element, "ReceiptText", out var receipt))
        {
            return null;
        }

        if (receipt.ValueKind == JsonValueKind.String)
        {
            return NormalizeOptional(receipt.GetString());
        }

        if (receipt.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var lines = receipt
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => NormalizeOptional(item.GetString()))
            .Where(line => line is not null)
            .Select(line => line!)
            .ToArray();
        return lines.Length == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static bool IsFinal(LinklyCloudBackendSessionResponse status)
    {
        return string.Equals(status.Status, StatusCompleted, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status.Status, StatusFailed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status.Status, StatusNotSubmitted, StringComparison.OrdinalIgnoreCase);
    }

    private static bool RequiresRecovery(LinklyCloudBackendSessionResponse status)
    {
        return string.Equals(status.Status, StatusTokenRefreshRequired, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status.RecoveryAction, RecoveryRefreshToken, StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(status.RecoveryAction, RecoveryRetry, StringComparison.OrdinalIgnoreCase) &&
                IsRecoveryHttpStatus(status.LastHttpStatus));
    }

    private async Task DelayBeforeNextPollAsync(
        LinklyCloudBackendSessionResponse status,
        CancellationToken cancellationToken)
    {
        var delay = GetNextPollDelay(status);
        Log($"poll delay start sessionId={status.SessionId} delayMs={delay.TotalMilliseconds:0} lastHttp={status.LastHttpStatus?.ToString(CultureInfo.InvariantCulture) ?? "<null>"}");
        var stopwatch = Stopwatch.StartNew();
        await DelayAsync(delay, cancellationToken);
        stopwatch.Stop();
        Log($"poll delay completed sessionId={status.SessionId} elapsedMs={stopwatch.ElapsedMilliseconds}");
    }

    private async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay > TimeSpan.Zero)
        {
            await _delayAsync(delay, cancellationToken);
        }
    }

    private TimeSpan GetNextPollDelay(LinklyCloudBackendSessionResponse status)
    {
        if (!IsRecoveryHttpStatus(status.LastHttpStatus))
        {
            return _pollInterval;
        }

        var exponent = Math.Clamp(status.RecoveryCount, 0, 6);
        var multiplier = 1 << exponent;
        var milliseconds = Math.Min(
            _pollInterval.TotalMilliseconds * multiplier,
            TimeSpan.FromSeconds(30).TotalMilliseconds);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static bool IsRecoveryHttpStatus(int? httpStatus)
    {
        return httpStatus == (int)HttpStatusCode.RequestTimeout ||
            httpStatus is >= 500 and <= 599;
    }

    private static IReadOnlyDictionary<string, string>? BuildPurchaseAnalysisData(
        decimal amount,
        PosSessionState session,
        string? refundReference)
    {
        if (string.IsNullOrWhiteSpace(refundReference))
        {
            return null;
        }

        return new Dictionary<string, string>
        {
            ["RFN"] = refundReference.Trim(),
            ["OPR"] = $"{session.CashierId}|{session.CashierName}",
            ["AMT"] = ToMinorUnits(amount).ToString("D9", CultureInfo.InvariantCulture),
            ["PCM"] = "0000"
        };
    }

    private static long ToMinorUnits(decimal amount)
    {
        return decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static string BuildTxnRef(PosSessionState session)
    {
        var device = new string(session.DeviceCode.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(device))
        {
            device = "POS";
        }

        return Limit($"{device}{DateTimeOffset.UtcNow:yyMMddHHmmss}", 16);
    }

    private static string? TryParseRefundReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var backendRefundReference = LinklyBackendPaymentReference.TryGetRefundReference(reference);
        if (!string.IsNullOrWhiteSpace(backendRefundReference))
        {
            return backendRefundReference;
        }

        var parts = reference.Trim().Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 &&
            string.Equals(parts[0], "ANZCLOUD", StringComparison.OrdinalIgnoreCase)
                ? parts[2]
                : null;
    }

    private static string? TryReadOriginalTxnRef(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var parts = reference.Trim().Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 &&
            string.Equals(parts[0], LinklyBackendPaymentReference.Prefix, StringComparison.OrdinalIgnoreCase)
                ? Uri.UnescapeDataString(parts[1])
                : null;
    }

    private string FormatResponseMessage(string? responseText, string? responseCode)
    {
        var text = NormalizeOptional(responseText);
        var code = NormalizeOptional(responseCode);
        if (text is null && code is null)
        {
            return T("linkly.backend.declined", "ANZ Linkly Cloud transaction was declined.");
        }

        return code is null ? text! : $"{text ?? T("linkly.backend.declined", "ANZ Linkly Cloud transaction was declined.")} ({code})";
    }

    private static JsonElement ReadResponse(JsonElement root)
    {
        return TryGetProperty(root, "Response", out var response) && response.ValueKind == JsonValueKind.Object
            ? response
            : root;
    }

    private static JsonElement ReadObject(JsonElement root, string propertyName)
    {
        return TryGetProperty(root, propertyName, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined ||
            !TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => NormalizeOptional(value.GetString()),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static decimal? ReadDecimal(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var decimalValue))
        {
            return decimalValue / 100m;
        }

        return value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out var parsed)
            ? parsed / 100m
            : null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? MaskCardNumber(string? pan)
    {
        var value = NormalizeOptional(pan);
        if (value is null)
        {
            return null;
        }

        if (value.Contains('*', StringComparison.Ordinal) || value.Contains('X', StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? digits : $"****{digits[^4..]}";
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string Limit(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static void Log(string message)
    {
        LinklyJsonLog.WriteMessage("LinklyBackend", "backend-terminal", message);
    }

    private string FormatRequestUrl(string relativeUrl)
    {
        return httpClient.BaseAddress is null
            ? relativeUrl
            : new Uri(httpClient.BaseAddress, relativeUrl).ToString();
    }

    private static string SerializeDebugJson<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static void LogHttpRequest(
        string operation,
        HttpMethod method,
        string url,
        string? txnType,
        string? txnRef,
        string? bodyJson)
    {
        var responseDetails = LinklyHttpEvidenceDetails.Empty;
        LinklyJsonLog.Write(
            "LinklyBackend",
            "backend-terminal",
            operation,
            "request",
            direction: "request",
            request: new
            {
                method = method.Method,
                url,
                body = RawJsonBody(bodyJson)
            },
            details: new
            {
                timestamp = DateTimeOffset.Now,
                certCase = GetCertificationCase(operation),
                transactionReference = ReadTransactionReference(url, bodyJson, responseDetails),
                txnType,
                txnRef,
                requestJson = bodyJson,
                responseJson = (string?)null
            });
    }

    private static void LogHttpResponse(
        string operation,
        HttpMethod method,
        string url,
        HttpStatusCode statusCode,
        long elapsedMs,
        string? txnType,
        string? txnRef,
        string? bodyJson)
    {
        var responseDetails = ReadLinklyHttpEvidenceDetails(bodyJson);
        LinklyJsonLog.Write(
            "LinklyBackend",
            "backend-terminal",
            operation,
            "response",
            direction: "response",
            httpStatus: statusCode,
            success: (int)statusCode is >= 200 and < 300,
            elapsedMs: elapsedMs,
            response: new
            {
                method = method.Method,
                url,
                body = RawJsonBody(bodyJson)
            },
            details: new
            {
                timestamp = DateTimeOffset.Now,
                certCase = GetCertificationCase(operation),
                transactionReference = ReadTransactionReference(url, null, responseDetails),
                txnType,
                txnRef = NormalizeOptional(txnRef) ?? responseDetails.TxnRef,
                requestJson = (string?)null,
                responseJson = bodyJson,
                responseTxnRef = responseDetails.TxnRef,
                responseDate = responseDetails.Date,
                responseTime = responseDetails.Time,
                responseCode = responseDetails.ResponseCode,
                responseText = responseDetails.ResponseText
            });
    }

    private static string? GetCertificationCase(string operation)
    {
        return operation switch
        {
            "transaction-status-test" => "3.1.1/3.1.2",
            "resumable session" => "4.1.2",
            "status" => "3.1.3/4.1.2",
            "recover" => "3.1.3/4.1.2",
            _ => null
        };
    }

    private static string? ReadTransactionReference(
        string url,
        string? requestJson,
        LinklyHttpEvidenceDetails responseDetails)
    {
        return NormalizeOptional(responseDetails.TxnRef) ??
            ReadSessionIdFromUrl(url) ??
            ReadSessionIdFromJson(requestJson);
    }

    private static string? ReadSessionIdFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], "transactions", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segments[i], "sessions", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(segments[i + 1]);
            }
        }

        return null;
    }

    private static string? ReadSessionIdFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return ReadString(document.RootElement, "SessionId");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LinklyHttpEvidenceDetails ReadLinklyHttpEvidenceDetails(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return LinklyHttpEvidenceDetails.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var response = TryGetProperty(root, "result", out var result) && result.ValueKind == JsonValueKind.Object
                ? result
                : root;
            if (TryGetProperty(response, "data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                response = data;
            }

            return new LinklyHttpEvidenceDetails(
                ReadString(response, "txnRef") ?? ReadString(response, "TxnRef") ?? ReadString(response, "responseTxnRef"),
                ReadString(response, "responseDate") ?? ReadString(response, "Date"),
                ReadString(response, "responseTime") ?? ReadString(response, "Time"),
                ReadString(response, "responseCode") ?? ReadString(response, "ResponseCode"),
                ReadString(response, "responseText") ?? ReadString(response, "ResponseText"));
        }
        catch (JsonException)
        {
            return LinklyHttpEvidenceDetails.Empty;
        }
    }

    private sealed record LinklyHttpEvidenceDetails(
        string? TxnRef,
        string? Date,
        string? Time,
        string? ResponseCode,
        string? ResponseText)
    {
        public static LinklyHttpEvidenceDetails Empty { get; } = new(null, null, null, null, null);
    }

    private static void LogStatusSnapshot(string prefix, LinklyCloudBackendSessionResponse status)
    {
        Log(
            $"{prefix} sessionId={status.SessionId} status={status.Status} lastHttp={status.LastHttpStatus?.ToString(CultureInfo.InvariantCulture) ?? "<null>"} " +
            $"txnRef={LogValue(status.TxnRef)} " +
            $"display=\"{LogValue(TruncateForLog(status.DisplayText, 80))}\" " +
            $"flags=cancel:{status.CancelKeyFlag},ok:{status.OKKeyFlag},yes:{status.AcceptYesKeyFlag},no:{status.DeclineNoKeyFlag},auth:{status.AuthoriseKeyFlag} " +
            $"notifications={status.Notifications?.Count ?? 0}");
    }

    private static string LogJsonBody(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<none>" : value.Trim();
    }

    private static object? RawJsonBody(string? bodyJson)
    {
        if (string.IsNullOrWhiteSpace(bodyJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(bodyJson);
            return JsonSerializer.Deserialize<object>(document.RootElement.GetRawText(), JsonOptions);
        }
        catch (JsonException)
        {
            return bodyJson;
        }
    }

    private static string LogValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<null>" : value.Trim();
    }

    private static string? TruncateForLog(string? value, int maxLength)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null || normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength];
    }

    private static string GetComponentVersion()
    {
        var assembly = typeof(LinklyBackendTerminalClient).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            assembly.GetName().Version?.ToString() ??
            "unknown";
    }

    private string T(string key, string fallback)
    {
        var value = localization?.T(key) ?? LocalizationResourceProvider.Instance[key];
        return string.IsNullOrWhiteSpace(value) || value.StartsWith("[[", StringComparison.Ordinal)
            ? fallback
            : value;
    }

    private sealed record BackendReadinessResult(bool IsReady, string Message)
    {
        public static BackendReadinessResult Ready { get; } = new(true, string.Empty);

        public static BackendReadinessResult NotReady(string message)
        {
            return new BackendReadinessResult(false, message);
        }
    }

    private sealed class LinklyBackendHttpException(
        string message,
        HttpStatusCode httpStatus) : HttpRequestException(message)
    {
        public HttpStatusCode HttpStatus { get; } = httpStatus;
    }
}
