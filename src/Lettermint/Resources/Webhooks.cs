using Lettermint.Internal;
using Lettermint.Models;

namespace Lettermint;

/// <summary>Delivery attempts of a webhook. Needs the team token.</summary>
public sealed class WebhookDeliveries : ApiResource
{
    internal WebhookDeliveries(Transport transport)
        : base(transport)
    {
    }

    /// <summary>Lists the deliveries of a webhook, one page at a time.</summary>
    public Task<ListWebhookDeliveriesResponse> ListAsync(string webhookId, ListWebhookDeliveriesQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.ListWebhookDeliveries, "Webhooks.Deliveries.ListAsync", [webhookId], query, default, options, cancellationToken);

    /// <summary>Iterates over every delivery of a webhook, following <c>next_cursor</c>.</summary>
    public IAsyncEnumerable<WebhookDeliveryListData> IterateAsync(string webhookId, ListWebhookDeliveriesQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.PaginateAsync<ListWebhookDeliveriesQuery, ListWebhookDeliveriesResponse, WebhookDeliveryListData>(Operations.ListWebhookDeliveries, "Webhooks.Deliveries.IterateAsync", [webhookId], query, options, cancellationToken);

    /// <summary>Retrieves one delivery attempt.</summary>
    public Task<WebhookDeliveryData> RetrieveAsync(string webhookId, string deliveryId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.GetWebhookDelivery, "Webhooks.Deliveries.RetrieveAsync", [webhookId, deliveryId], default, default, options, cancellationToken);
}

/// <summary>Webhook endpoints. Needs the team token. To verify incoming deliveries, use <see cref="Webhook"/>.</summary>
public sealed class Webhooks : ApiResource
{
    internal Webhooks(Transport transport)
        : base(transport)
    {
        Deliveries = new WebhookDeliveries(transport);
    }

    /// <summary>Delivery attempts of a webhook.</summary>
    public WebhookDeliveries Deliveries { get; }

    /// <summary>Lists webhooks, one page at a time.</summary>
    public Task<ListWebhooksResponse> ListAsync(ListWebhooksQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.ListWebhooks, "Webhooks.ListAsync", [], query, default, options, cancellationToken);

    /// <summary>Iterates over every webhook, following <c>next_cursor</c>.</summary>
    public IAsyncEnumerable<WebhookListData> IterateAsync(ListWebhooksQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.PaginateAsync<ListWebhooksQuery, ListWebhooksResponse, WebhookListData>(Operations.ListWebhooks, "Webhooks.IterateAsync", [], query, options, cancellationToken);

    /// <summary>Creates a webhook. The response holds its signing secret once.</summary>
    public Task<WebhookSecretResponse> CreateAsync(StoreWebhookData body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.CreateWebhook, "Webhooks.CreateAsync", [], default, body, options, cancellationToken);

    /// <summary>Retrieves a webhook.</summary>
    public Task<WebhookData> RetrieveAsync(string webhookId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.GetWebhook, "Webhooks.RetrieveAsync", [webhookId], default, default, options, cancellationToken);

    /// <summary>Updates a webhook.</summary>
    public Task<WebhookMutationResponse> UpdateAsync(string webhookId, UpdateWebhookData body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.UpdateWebhook, "Webhooks.UpdateAsync", [webhookId], default, body, options, cancellationToken);

    /// <summary>Deletes a webhook.</summary>
    public Task<MessageResponse> DeleteAsync(string webhookId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.DeleteWebhook, "Webhooks.DeleteAsync", [webhookId], default, default, options, cancellationToken);

    /// <summary>Sends a <c>webhook.test</c> delivery.</summary>
    public Task<TestWebhookResponse> TestAsync(string webhookId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.TestWebhook, "Webhooks.TestAsync", [webhookId], default, default, options, cancellationToken);

    /// <summary>Replaces the signing secret. The response holds the new secret once.</summary>
    public Task<WebhookSecretResponse> RegenerateSecretAsync(string webhookId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.RegenerateWebhookSecret, "Webhooks.RegenerateSecretAsync", [webhookId], default, default, options, cancellationToken);
}
