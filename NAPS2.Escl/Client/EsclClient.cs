using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NAPS2.Escl.Client;

public class EsclClient
{
    private static readonly XNamespace ScanNs = EsclXmlHelper.ScanNs;
    private static readonly XNamespace PwgNs = EsclXmlHelper.PwgNs;

    // Clients that verify HTTPS certificates
    private static readonly HttpClient VerifiedHttpClient = new();
    private static readonly HttpClient VerifiedProgressHttpClient = new();
    private static readonly HttpClient VerifiedDocumentHttpClient = new();

    // Clients that don't verify HTTPS certificates
    private static readonly HttpClientHandler UnverifiedHttpClientHandler = new()
    {
        // ESCL certificates are generally self-signed - we aren't trying to verify server authenticity, just ensure
        // that the connection is encrypted and protect against passive interception.
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
    };
    // Sadly as we're still using .NET Framework on Windows, we're stuck with the old HttpClient implementation, which
    // has trouble with concurrency. So we use a separate client for long running requests (Progress/NextDocument).
    private static readonly HttpClient UnverifiedHttpClient = new(UnverifiedHttpClientHandler);
    private static readonly HttpClient UnverifiedProgressHttpClient = new(UnverifiedHttpClientHandler);
    private static readonly HttpClient UnverifiedDocumentHttpClient = new(UnverifiedHttpClientHandler);

    private readonly EsclService _service;
    private bool _httpFallback;

    public EsclClient(EsclService service)
    {
        _service = service;
    }

    public EsclSecurityPolicy SecurityPolicy { get; set; }

    public ILogger Logger { get; set; } = NullLogger.Instance;

    public CancellationToken CancelToken { get; set; }

    private HttpClient HttpClient => SecurityPolicy.HasFlag(EsclSecurityPolicy.ClientRequireHttpOrTrustedCertificate)
        ? VerifiedHttpClient
        : UnverifiedHttpClient;

    private HttpClient ProgressHttpClient =>
        SecurityPolicy.HasFlag(EsclSecurityPolicy.ClientRequireHttpOrTrustedCertificate)
            ? VerifiedProgressHttpClient
            : UnverifiedProgressHttpClient;

    private HttpClient DocumentHttpClient =>
        SecurityPolicy.HasFlag(EsclSecurityPolicy.ClientRequireHttpOrTrustedCertificate)
            ? VerifiedDocumentHttpClient
            : UnverifiedDocumentHttpClient;

    public async Task<EsclCapabilities> GetCapabilities()
    {
        var doc = await DoRequest("ScannerCapabilities");
        return CapabilitiesParser.Parse(doc);
    }

    public async Task<EsclScannerStatus> GetStatus()
    {
        var doc = await DoRequest("ScannerStatus");
        var root = doc.Root;
        if (root?.Name != ScanNs + "ScannerStatus")
        {
            throw new InvalidOperationException("Unexpected root element: " + doc.Root?.Name);
        }
        var jobStates = new Dictionary<string, EsclJobState>();
        foreach (var jobInfoEl in root.Element(ScanNs + "Jobs")?.Elements(ScanNs + "JobInfo") ?? [])
        {
            var jobUri = jobInfoEl.Element(PwgNs + "JobUri")?.Value;
            var jobState = ParseHelper.MaybeParseEnum(jobInfoEl.Element(PwgNs + "JobState"), EsclJobState.Unknown);
            if (jobUri != null && jobState != EsclJobState.Unknown)
            {
                jobStates.Add(jobUri, jobState);
            }
        }
        return new EsclScannerStatus
        {
            State = ParseHelper.MaybeParseEnum(root.Element(PwgNs + "State"), EsclScannerState.Unknown),
            AdfState = ParseHelper.MaybeParseEnum(root.Element(ScanNs + "AdfState"), EsclAdfState.Unknown),
            JobStates = jobStates
        };
    }

    public async Task<EsclJob> CreateScanJob(EsclScanSettings settings)
    {
        var doc =
            EsclXmlHelper.CreateDocAsString(
                new XElement(ScanNs + "ScanSettings",
                    new XElement(PwgNs + "Version", "2.0"),
                    new XElement(ScanNs + "Intent", "TextAndGraphic"),
                    new XElement(PwgNs + "ScanRegions",
                        new XAttribute(PwgNs + "MustHonor", "true"),
                        new XElement(PwgNs + "ScanRegion",
                            new XElement(PwgNs + "Height", settings.Height),
                            new XElement(PwgNs + "ContentRegionUnits", "escl:ThreeHundredthsOfInches"),
                            new XElement(PwgNs + "Width", settings.Width),
                            new XElement(PwgNs + "XOffset", settings.XOffset),
                            new XElement(PwgNs + "YOffset", settings.YOffset))),
                    new XElement(PwgNs + "InputSource", settings.InputSource),
                    new XElement(ScanNs + "Duplex", settings.Duplex),
                    new XElement(ScanNs + "ColorMode", settings.ColorMode),
                    new XElement(ScanNs + "XResolution", settings.XResolution),
                    new XElement(ScanNs + "YResolution", settings.YResolution),
                    // TODO: Brightness/contrast/threshold
                    // new XElement(ScanNs + "Brightness", settings.Brightness),
                    // new XElement(ScanNs + "Contrast", settings.Contrast),
                    // new XElement(ScanNs + "Threshold", settings.Threshold),
                    OptionalElement(ScanNs + "CompressionFactor", settings.CompressionFactor),
                    new XElement(PwgNs + "DocumentFormat", settings.DocumentFormat)));
        var content = new StringContent(doc, Encoding.UTF8, "text/xml");
        var response = await WithHttpFallback(
            () => GetUrl($"/{_service.RootUrl}/ScanJobs"),
            url =>
            {
                Logger.LogDebug("ESCL POST {Url}", url);
                return HttpClient.PostAsync(url, content);
            });
        response.EnsureSuccessStatusCode();
        Logger.LogDebug("POST OK");

        var uri = response.Headers.Location!;

        return new EsclJob
        {
            UriPath = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString
        };
    }

    private XElement? OptionalElement(XName elementName, int? value)
    {
        if (value == null) return null;
        return new XElement(elementName, value);
    }

    public async Task<RawDocument?> NextDocument(EsclJob job, Action<double>? pageProgress = null)
    {
        var progressCts = new CancellationTokenSource();
        if (pageProgress != null)
        {
            var progressUrl = GetUrl($"{job.UriPath}/Progress");
            var progressResponse = await ProgressHttpClient.GetStreamAsync(progressUrl);
            var streamReader = new StreamReader(progressResponse);
            _ = Task.Run(async () =>
            {
                using var streamReaderForDisposal = streamReader;
                while (await streamReader.ReadLineAsync() is { } line)
                {
                    if (progressCts.IsCancellationRequested)
                    {
                        return;
                    }
                    if (double.TryParse(line, NumberStyles.Any, CultureInfo.InvariantCulture, out var progress))
                    {
                        pageProgress(progress);
                    }
                }
            });
        }
        try
        {
            // TODO: Maybe check Content-Location on the response header to ensure no duplicate document?
            var response = await WithHttpFallback(
                () => GetUrl($"{job.UriPath}/NextDocument"),
                url =>
                {
                    Logger.LogDebug("ESCL GET {Url}", url);
                    return DocumentHttpClient.GetAsync(url);
                });
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                // NotFound = end of scan, Gone = canceled
                Logger.LogDebug("GET failed: {Status}", response.StatusCode);
                return null;
            }
            response.EnsureSuccessStatusCode();
            var doc = new RawDocument
            {
                Data = await response.Content.ReadAsByteArrayAsync(),
                ContentType = response.Content.Headers.ContentType?.MediaType,
                ContentLocation = response.Content.Headers.ContentLocation?.ToString()
            };
            Logger.LogDebug("GET OK: {Type} ({Bytes} bytes) {Location}", doc.ContentType, doc.Data.Length,
                doc.ContentLocation);
            return doc;
        }
        finally
        {
            progressCts.Cancel();
        }
    }

    public async Task<string> ErrorDetails(EsclJob job)
    {
        var response = await WithHttpFallback(
            () => GetUrl($"{job.UriPath}/ErrorDetails"),
            url =>
            {
                Logger.LogDebug("ESCL GET {Url}", url);
                return HttpClient.GetAsync(url);
            });
        response.EnsureSuccessStatusCode();
        Logger.LogDebug("GET OK");
        return await response.Content.ReadAsStringAsync();
    }

    public async Task CancelJob(EsclJob job)
    {
        var response = await WithHttpFallback(
            () => GetUrl(job.UriPath),
            url =>
            {
                Logger.LogDebug("ESCL DELETE {Url}", url);
                return HttpClient.DeleteAsync(url);
            });
        if (!response.IsSuccessStatusCode)
        {
            Logger.LogDebug("DELETE failed: {Status}", response.StatusCode);
            return;
        }
        response.EnsureSuccessStatusCode();
        Logger.LogDebug("DELETE OK");
    }

    private async Task<XDocument> DoRequest(string endpoint)
    {
        // TODO: Retry logic
        var response = await WithHttpFallback(
            () => GetUrl($"/{_service.RootUrl}/{endpoint}"),
            url =>
            {
                Logger.LogDebug("ESCL GET {Url}", url);
                return HttpClient.GetAsync(url, CancelToken);
            });
        response.EnsureSuccessStatusCode();
        Logger.LogDebug("GET OK");
        var text = await response.Content.ReadAsStringAsync();
        var doc = XDocument.Parse(text);
        return doc;
    }

    private async Task<T> WithHttpFallback<T>(Func<string> urlFunc, Func<string, Task<T>> func)
    {
        string url = urlFunc();
        try
        {
            return await func(url);
        }
        catch (HttpRequestException ex) when (!SecurityPolicy.HasFlag(EsclSecurityPolicy.ClientRequireHttps) &&
                                              !_httpFallback &&
                                              url.StartsWith("https://") && (
                                                  ex.InnerException is AuthenticationException ||
                                                  ex.InnerException?.InnerException is AuthenticationException))
        {
            Logger.LogDebug(ex, "TLS authentication error; falling back to HTTP");
            _httpFallback = true;
            url = urlFunc();
            return await func(url);
        }
    }

    private string GetUrl(string endpoint)
    {
        bool tls = (_service.Tls || _service.Port == 443) && !_httpFallback;
        if (SecurityPolicy.HasFlag(EsclSecurityPolicy.ClientRequireHttps) && !tls)
        {
            throw new EsclSecurityPolicyViolationException(
                $"EsclSecurityPolicy of {SecurityPolicy} doesn't allow HTTP connections");
        }
        var protocol = tls ? "https" : "http";
        return $"{protocol}://{GetHostAndPort(_service.Tls && !_httpFallback)}{endpoint}";
    }

    private string GetHostAndPort(bool tls)
    {
        var port = tls ? _service.TlsPort : _service.Port;
        var host = new IPEndPoint(_service.RemoteEndpoint, port).ToString();
#if NET6_0_OR_GREATER
        if (OperatingSystem.IsMacOS())
        {
            // Using the mDNS hostname is more reliable on Mac (but doesn't work at all on Windows)
            host = $"{_service.Host}:{port}";
        }
#endif
        return host;
    }
}