using System;
using System.Linq;
using System.Collections.Generic;

public enum Error : UInt32
{
    Success = 0x80000000, // S_OK
    Unknown = 0x8c5c0001, // c5c is the HRESULT facility for CSChat, the remaining bits are the error code
    ProviderNotConfigured,
    ModelNotFound,
    DirectoryNotFound,
    ChunkerNotConfigured,
    ToolNotFound,
    ToolNotAvailable,
    ToolFailed,
    FailedToParseResponse,
    PathNotFound,
    EmptyResponse,
    InvalidInput,
    PlanningFailed,
    NoMatchesFound,
    ConnectionFailed,
    ProcessStartFailed,
    InitializationFailed,
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