namespace Client.Models
{
    public class Result<T>
    {
        public bool Success { get; init; }
        public T? Value { get; init; }
        public string? Error { get; init; }

        public static Result<T> Ok(T value) => new() { Success = true, Value = value };
        public static Result<T> Fail(string message) => new() { Success = false, Error = message };
    }
}
