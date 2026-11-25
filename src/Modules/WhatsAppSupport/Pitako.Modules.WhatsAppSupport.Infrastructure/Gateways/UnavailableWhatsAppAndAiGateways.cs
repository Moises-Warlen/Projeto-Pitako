using System.Security.Cryptography;
using System.Text;
using Pitako.Modules.WhatsAppSupport.Application.Abstractions;
using Pitako.Modules.WhatsAppSupport.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.WhatsAppSupport.Infrastructure.Gateways;

/// <summary>
/// Gateway padrão da Meta Cloud API com verificação real HMAC X-Hub-Signature-256
/// e falha explícita no envio HTTP quando credenciais reais de nuvem não estão configuradas (Seção 29).
/// </summary>
public sealed class UnavailableWhatsAppCloudGateway : IWhatsAppCloudGateway
{
    public bool VerifyPayloadSignature(string rawBody, string? xHubSignature256, string decryptedAppSecret)
    {
        if (string.IsNullOrWhiteSpace(xHubSignature256) || string.IsNullOrWhiteSpace(decryptedAppSecret))
            return false;

        var computed = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(decryptedAppSecret.Trim()),
            Encoding.UTF8.GetBytes(rawBody ?? string.Empty));
        var expectedHex = Convert.ToHexString(computed).ToLowerInvariant();
        var cleaned = xHubSignature256.Trim().Replace("sha256=", string.Empty, StringComparison.OrdinalIgnoreCase).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedHex),
            Encoding.ASCII.GetBytes(cleaned.Length == expectedHex.Length ? cleaned : new string('0', expectedHex.Length)));
    }

    public Task<Result<string>> SendTextMessageAsync(
        WhatsAppChannelConfig channel,
        string toPhone,
        string text,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<string>(Error.Conflict(
            "WhatsApp.CloudCredentialsNotConfigured",
            $"Envio HTTP oficial Meta Cloud API para {toPhone} (canal {channel.DisplayPhoneNumber}) indisponível sem token ativo de produção.")));
}

/// <summary>
/// Gateway padrão quando o provedor externo de IA generativa não possui chave configurada no ambiente.
/// Nunca inventa respostas: falha explicitamente para que a conversa siga para transbordo humano seguro.
/// </summary>
public sealed class UnavailableExternalAiGateway : IAiAssistantGateway
{
    public Task<Result<string>> GenerateGroundedReplyAsync(
        GroundedTenantKnowledgeSnapshot snapshot,
        string customerMessage,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<string>(Error.Conflict(
            "Ai.ExternalProviderNotConfigured",
            "Provedor de IA generativa não configurado neste ambiente; conversa encaminhada para atendimento humano.")));
}
