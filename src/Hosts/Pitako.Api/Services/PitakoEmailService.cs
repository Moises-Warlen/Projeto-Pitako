using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Pitako.Modules.MasterPanel.Application.Abstractions;
using Pitako.Modules.MasterPanel.Domain;
using Pitako.SharedKernel.Time;

namespace Pitako.Api.Services;

public sealed record EmailSmtpSettingsDto(
    string Host,
    int Port,
    bool UseSsl,
    string Username,
    string Password,
    string FromEmail,
    string FromName,
    string PublicWebBaseUrl);

public sealed record EmailOutboxEntry(
    Guid Id,
    DateTimeOffset SentAtUtc,
    string ToEmail,
    string Subject,
    string BodyText,
    string Kind,
    string DeliveryStatus,
    string? ErrorDetail = null);

public sealed class PitakoEmailService(
    IGlobalSettingRepository settingRepo,
    IConfiguration configuration,
    IClock clock,
    ILogger<PitakoEmailService> logger)
{
    public const string SmtpSettingKey = "email:smtp";
    public const string OutboxSettingKey = "email:outbox";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<EmailSmtpSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        var saved = await settingRepo.FindByKeyAsync(SmtpSettingKey, ct);
        if (saved is not null && !string.IsNullOrWhiteSpace(saved.Value))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<EmailSmtpSettingsDto>(saved.Value, JsonOpts);
                if (parsed is not null)
                    return parsed;
            }
            catch
            {
                // fallback to config
            }
        }

        return new EmailSmtpSettingsDto(
            Host: configuration["Smtp:Host"] ?? string.Empty,
            Port: int.TryParse(configuration["Smtp:Port"], out var p) ? p : 587,
            UseSsl: !bool.TryParse(configuration["Smtp:UseSsl"], out var ssl) || ssl,
            Username: configuration["Smtp:Username"] ?? string.Empty,
            Password: configuration["Smtp:Password"] ?? string.Empty,
            FromEmail: configuration["Smtp:FromEmail"] ?? "noreply@pitako.com.br",
            FromName: configuration["Smtp:FromName"] ?? "Plataforma PITAKO",
            PublicWebBaseUrl: configuration["Smtp:PublicWebBaseUrl"] ?? "http://localhost:3000");
    }

    public async Task SaveSettingsAsync(EmailSmtpSettingsDto settings, Guid? updatedByUserId, CancellationToken ct = default)
    {
        var normalized = settings with
        {
            Host = (settings.Host ?? string.Empty).Trim(),
            Port = settings.Port > 0 ? settings.Port : 587,
            Username = (settings.Username ?? string.Empty).Trim(),
            FromEmail = string.IsNullOrWhiteSpace(settings.FromEmail) ? "noreply@pitako.com.br" : settings.FromEmail.Trim(),
            FromName = string.IsNullOrWhiteSpace(settings.FromName) ? "Plataforma PITAKO" : settings.FromName.Trim(),
            PublicWebBaseUrl = string.IsNullOrWhiteSpace(settings.PublicWebBaseUrl)
                ? "http://localhost:3000"
                : settings.PublicWebBaseUrl.Trim().TrimEnd('/')
        };

        var json = JsonSerializer.Serialize(normalized, JsonOpts);
        await UpsertSettingAsync(SmtpSettingKey, json, "Configuração SMTP da Plataforma", clock.UtcNow, ct);
    }

    private async Task UpsertSettingAsync(string key, string value, string description, DateTimeOffset utcNow, CancellationToken ct)
    {
        var existing = await settingRepo.FindByKeyAsync(key, ct);
        if (existing is null)
        {
            var item = new GlobalPlatformSetting(Guid.NewGuid(), key, value, description, false);
            item.SetCreatedAudit(utcNow);
            await settingRepo.AddAsync(item, ct);
        }
        else
        {
            existing.UpdateValue(value);
        }
        await settingRepo.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<EmailOutboxEntry>> ListOutboxAsync(CancellationToken ct = default)
    {
        var saved = await settingRepo.FindByKeyAsync(OutboxSettingKey, ct);
        if (saved is null || string.IsNullOrWhiteSpace(saved.Value))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<EmailOutboxEntry>>(saved.Value, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<EmailOutboxEntry> SendAsync(
        string toEmail,
        string subject,
        string bodyText,
        string? bodyHtml = null,
        string kind = "Transactional",
        CancellationToken ct = default)
    {
        var cleanTo = (toEmail ?? string.Empty).Trim();
        var cfg = await GetSettingsAsync(ct);
        var now = clock.UtcNow;

        string status;
        string? errorDetail = null;

        if (!string.IsNullOrWhiteSpace(cfg.Host) && !string.IsNullOrWhiteSpace(cleanTo))
        {
            try
            {
                using var message = new MailMessage
                {
                    From = new MailAddress(cfg.FromEmail, cfg.FromName),
                    Subject = subject,
                    Body = string.IsNullOrWhiteSpace(bodyHtml) ? bodyText : bodyHtml,
                    IsBodyHtml = !string.IsNullOrWhiteSpace(bodyHtml)
                };
                message.To.Add(cleanTo);

                using var client = new SmtpClient(cfg.Host, cfg.Port)
                {
                    EnableSsl = cfg.UseSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 10_000
                };

                if (!string.IsNullOrWhiteSpace(cfg.Username))
                {
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential(cfg.Username, cfg.Password);
                }

                await client.SendMailAsync(message, ct);
                status = "SentSmtp";
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha SMTP ao enviar e-mail para {Email}; gravado na caixa de saída.", cleanTo);
                status = "QueuedOutboxFallback";
                errorDetail = ex.Message;
            }
        }
        else
        {
            status = "DeliveredToOutbox";
            logger.LogInformation("E-mail [{Kind}] para {Email}: {Subject}\n{Body}", kind, cleanTo, subject, bodyText);
        }

        var entry = new EmailOutboxEntry(Guid.NewGuid(), now, cleanTo, subject, bodyText, kind, status, errorDetail);
        var current = (await ListOutboxAsync(ct)).ToList();
        current.Insert(0, entry);
        if (current.Count > 100)
            current = current.Take(100).ToList();

        await UpsertSettingAsync(
            OutboxSettingKey,
            JsonSerializer.Serialize(current, JsonOpts),
            "Histórico de e-mails enviados",
            now,
            ct);
        return entry;
    }

    public string ResolveWebBaseUrl(EmailSmtpSettingsDto cfg, HttpRequest? req = null)
    {
        if (req is not null)
        {
            var origin = req.Headers.Origin.ToString();
            if (!string.IsNullOrWhiteSpace(origin))
                return origin.TrimEnd('/');
        }
        return string.IsNullOrWhiteSpace(cfg.PublicWebBaseUrl) ? "http://localhost:3000" : cfg.PublicWebBaseUrl.TrimEnd('/');
    }
}
