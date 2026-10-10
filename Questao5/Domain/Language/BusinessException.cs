namespace Questao5.Domain.Language;

public sealed class BusinessException : Exception
{
    public string Type { get; }
    public int StatusCode { get; }

    public BusinessException(string type, string message, int statusCode = 400) : base(message)
    {
        Type = type;
        StatusCode = statusCode;
    }
}
