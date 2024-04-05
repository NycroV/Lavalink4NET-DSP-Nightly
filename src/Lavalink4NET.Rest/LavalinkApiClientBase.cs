﻿namespace Lavalink4NET.Rest;

using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public abstract class LavalinkApiClientBase
{
    private const string AllowedPassphraseCharacters = " ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.~!#$&'()*+,/:;=?@[]";

    private static int _insecurePassphraseNoticeSent;

    private readonly IOptions<LavalinkApiClientOptions> _options;
    private readonly ILogger<LavalinkApiClientBase> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _authenticationHeaderValue;

    protected LavalinkApiClientBase(
        IHttpClientFactory httpClientFactory,
        IOptions<LavalinkApiClientOptions> options,
        ILogger<LavalinkApiClientBase> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Value.Passphrase.Equals("youshallnotpass", StringComparison.Ordinal) &&
            Interlocked.CompareExchange(ref _insecurePassphraseNoticeSent, 1, 0) is 0)
        {
            logger.LogWarning("The default Lavalink password is currently being used. It is highly recommended to change the password immediately to enhance the security of your system.");
        }

        if (options.Value.Passphrase.Any(x => !AllowedPassphraseCharacters.Contains(x)))
        {
            throw new ArgumentException($"The passphrase contains invalid characters. The following characters are allowed: '{AllowedPassphraseCharacters}'", nameof(options));
        }

        _options = options;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _authenticationHeaderValue = options.Value.Passphrase;
    }

    public HttpClient CreateHttpClient()
    {
        var httpClient = _httpClientFactory.CreateClient(_options.Value.HttpClientName);
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", _authenticationHeaderValue);
        return httpClient;
    }
}
