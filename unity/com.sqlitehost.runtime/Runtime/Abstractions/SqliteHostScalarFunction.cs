using System;

namespace SqliteHost
{
    /// <summary>
    /// One registerable inline scalar function (feature inlineFunctions,
    /// docs/adapter-contract.md). The shape holds the full arity range;
    /// registering the function for every arity in MinArgs..MaxArgs against
    /// the native wrapper is the adapter's job.
    /// </summary>
    public sealed class SqliteHostScalarFunction
    {
        /// <summary>
        /// Marker prefix an adapter must put on the SQL error text when
        /// <see cref="Invoke"/> throws, so the runtime can map the failed
        /// statement back to FailedHandler/handler-error. An exception must
        /// never cross the native frames (IL2CPP safety).
        /// </summary>
        public const string HandlerErrorMarker = "SQLITEHOST_HANDLER_ERROR:";

        public SqliteHostScalarFunction(
            string name,
            int minArgs,
            int maxArgs,
            Func<SqliteHostBindingValue[], SqliteHostBindingValue> invoke)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name must be non-empty.", nameof(name));
            }
            if (minArgs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minArgs), "minArgs must not be negative.");
            }
            if (maxArgs < minArgs)
            {
                throw new ArgumentOutOfRangeException(nameof(maxArgs), "maxArgs must not be below minArgs.");
            }
            if (invoke == null)
            {
                throw new ArgumentNullException(nameof(invoke));
            }
            Name = name;
            MinArgs = minArgs;
            MaxArgs = maxArgs;
            Invoke = invoke;
        }

        public string Name { get; }

        public int MinArgs { get; }

        public int MaxArgs { get; }

        /// <summary>
        /// The scalar handler. args.Length is the invoked arity, within
        /// [MinArgs..MaxArgs]; omitted trailing args are treated as null by
        /// the consumer (the runtime maps them to null DTO fields).
        /// </summary>
        public Func<SqliteHostBindingValue[], SqliteHostBindingValue> Invoke { get; }
    }
}
