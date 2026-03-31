

namespace ClipBoard.Models;


public class OperationalResult
{
    public bool IsSuccess { get; protected set; }
    public string? UserMessage { get; protected set; }
    public ErrorContext? ErrorContext { get; protected set; }

    protected OperationalResult() { }

    protected OperationalResult(bool isSuccess, string? userMessage = null, ErrorContext? errorContext = null)
    {
        IsSuccess = isSuccess;
        UserMessage = userMessage;
        ErrorContext = errorContext;
    }

    public static OperationalResult Success()
        => new(true);

    public static OperationalResult Failure(string userMessage, ErrorType type, Exception? ex = null)
        => new (false, userMessage, new ErrorContext(type, ex));

    public bool IsFailure => !IsSuccess;
}

public class ErrorContext(ErrorType type, Exception? ex = null)
{
    public ErrorType ErrorType { get; set; } = type;
    public string? TechnicalMessage { get; set; } = ex?.Message;
    public string? StackTrace { get; set; } = ex?.StackTrace;
    public Exception? InnerException { get; set; } = ex?.InnerException;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> AdditionalData { get; set; } = [];
}

public class OperationalResult<TValue> : OperationalResult
{
    public TValue? Value { get; private set; }

    private OperationalResult() { }

    private OperationalResult(TValue value) : base(true){ Value = value;}

    private OperationalResult(string userMessage, ErrorContext errorContext) 
        : base(false, userMessage, errorContext)
    {
        Value = default;
    }

    public static OperationalResult<TValue> Success(TValue value)
        => new(value);

    public static new OperationalResult<TValue> Failure(string userMessage, ErrorType type, Exception? ex = null)
        => new (userMessage, new ErrorContext(type, ex));


    public static implicit operator OperationalResult<TValue>(TValue value)
        => Success(value);

    public static implicit operator TValue?(OperationalResult<TValue> result)
        => result.IsSuccess ? result.Value : default;

    public bool TryGetValue([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TValue? value)
    {
        if (IsSuccess && Value != null)
        {
            value = Value;
            return true;
        }

        value = default;
        return false;
    }
    public bool HasValue => IsSuccess && Value != null;
}


public enum ErrorType
{
    Iternal,
    NotFound,
    Null,
    NetWork,
    Conflict
}