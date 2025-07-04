using System;
using System.Linq;
using System.Collections.Generic;

public enum Error : UInt32
{
    Success = 0,
    Unknown,
    ProviderNotConfigured,
    ModelNotFound,
    DirectoryNotFound,
    ChunkerNotConfigured,
}

public class CsChatException : Exception
{
    public Error ErrorCode { get; }

    public CsChatException(string message, Error errorCode = Error.Unknown) : base(message)
    {
        ErrorCode = errorCode;
    }

    public CsChatException(string message, Exception innerException, Error errorCode = Error.Unknown) 
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}