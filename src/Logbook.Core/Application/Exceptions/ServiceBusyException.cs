// Copyright(c) Alan Dutton.
// Licensed under the MIT License.
// See the LICENSE file in the project root for license information.

namespace Logbook.Core.Application.Exceptions;

/// <summary>
///     Thrown when a valid request cannot be handled right now and should be sent again later, e.g. because the queue of
///     received Log Events is full. The API returns 503 Service Unavailable with a Retry-After header.
/// </summary>
public sealed class ServiceBusyException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ServiceBusyException"/> class.
    /// </summary>
    public ServiceBusyException()
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ServiceBusyException"/> class with a message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ServiceBusyException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ServiceBusyException"/> class with a message and an inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public ServiceBusyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
