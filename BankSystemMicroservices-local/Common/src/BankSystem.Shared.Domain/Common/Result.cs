namespace BankSystem.Shared.Domain.Common;

/// <summary>
/// Represents a result of an operation with success/failure indication
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string Error { get; }
    public ErrorType? ErrorType { get; }
    public string? ErrorCode { get; }

    protected Result(bool isSuccess, string error, ErrorType? errorType = null, string? errorCode = null)
    {
        IsSuccess = isSuccess;
        Error = error;
        ErrorType = errorType;
        ErrorCode = errorCode;
    }

    public static Result Success() => new(true, string.Empty);
    public static Result Failure(
        string error,
        ErrorType errorType = Common.ErrorType.Failure,
        string? errorCode = null) => new(false, error, errorType, errorCode);
}

/// <summary>
/// Represents a result of an operation with a value
/// </summary>
public class Result<T> : Result
{
    public T? Value { get; }

    protected Result(bool isSuccess, T? value, string error, ErrorType? errorType = null, string? errorCode = null)
        : base(isSuccess, error, errorType, errorCode)
    {
        Value = value;
    }

    public static Result<T> Success(T value) => new(true, value, string.Empty);
    public new static Result<T> Failure(
        string error,
        ErrorType errorType = Common.ErrorType.Failure,
        string? errorCode = null) => new(false, default, error, errorType, errorCode);
}
