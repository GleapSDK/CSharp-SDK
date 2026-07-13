namespace GleapSDK.Http;

/// <summary>Immutable HTTP response value: status code + raw body.</summary>
public readonly struct HttpResult
{
    public int StatusCode { get; }
    public string Body { get; }
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    public HttpResult(int statusCode, string body)
    {
        StatusCode = statusCode;
        Body = body;
    }
}
