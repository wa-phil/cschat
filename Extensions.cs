using System;


static public class Extensions
{
    /// <summary>
    /// Throws the specified exception if we should.<br/>
    /// </summary>
    /// <param name="shouldThrow">true if an exception should be thrown, false otherwise.</param>
    /// <param name="exception">The exception to be thrown.</param>
    static private void ThrowIf(bool shouldThrow, Exception exception) { if (shouldThrow) { throw exception; } }    

    /// <summary>
    /// Throws an CsChatException with the specified message and error code if this string is null or empty. <br/>
    /// </summary>
    /// <param name="value">The value to check. If it evalutes to null or is the empty string, this method will throw.</param>
    /// <param name="message">The associated error message, should this method throw.</param>
    /// <param name="errorCode">The error code that should be attached to this exception.</param>
    static public void ThrowIfNullOrEmpty(this string value, string message, Error errorCode = Error.Unknown) 
        => ThrowIf(string.IsNullOrEmpty(value), new CsChatException(message, errorCode));

    /// <summary>
    /// Throws an CsChatException with the specified message and error code if this enumerable object is null or contains no elements.<br/>
    /// </summary>
    /// <param name="value">The value to check. If it evalutes to null or is contains no elements, this method will throw.</param>
    /// <param name="message">The associated error message, should this method throw.</param>
    /// <param name="errorCode">The error code that should be attached to this exception.</param>
    static public void ThrowIfNullOrEmpty<T>(this IEnumerable<T> value, string message, Error errorCode = Error.Unknown)
        => ThrowIf(null == value || !value.Any(), new CsChatException(message, errorCode));

    /// <summary>
    /// Throws an CsChatException with the specified message and error code if this object is null. <br/>
    /// </summary>
    /// <param name="value">The value to check. If it evalutes to null, this method will throw.</param>
    /// <param name="message">The associated error message, should this method throw.</param>
    /// <param name="errorCode">The error code that should be attached to this exception.</param>
    static public void ThrowIfNull<T>(this T value, string message, Error errorCode = Error.Unknown)
        => ThrowIf(null == value, new CsChatException(message, errorCode));

    /// <summary>
    /// Throws an CsChatException with the specified message and error code if this condition evaluates to true.<br/>
    /// </summary>
    /// <param name="value">The value to check. If it evalutes to true, this method will throw.</param>
    /// <param name="message">The associated error message, should this method throw.</param>
    /// <param name="errorCode">The error code that should be attached to this exception.</param>
    static public void ThrowIfTrue(this bool value, string message, Error errorCode = Error.Unknown)
        => ThrowIf(value, new CsChatException(message, errorCode));

    /// <summary>
    /// Throws an CsChatException with the specified error message and error code if this condition evaluates to false.<br/>
    /// </summary>
    /// <param name="value">The value to check. If it evalutes to false, this method will throw.</param>
    /// <param name="message">The associated error message, should this method throw.</param>
    /// <param name="errorCode">The error code that should be attached to this exception.</param>
    static public void ThrowIfFalse(this bool value, string message, Error errorCode = Error.Unknown)
        => ThrowIf(!value, new CsChatException(message, errorCode));

}