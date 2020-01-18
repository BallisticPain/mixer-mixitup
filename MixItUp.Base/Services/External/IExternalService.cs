﻿using System;
using System.Threading.Tasks;

namespace MixItUp.Base.Services.External
{
    public interface IExternalService
    {
        string Name { get; }

        bool IsConnected { get; }

        Task<ExternalServiceResult> Connect();

        Task Disconnect();
    }

    public class ExternalServiceResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public Exception Exception { get; set; }

        public ExternalServiceResult() : this(true) { }

        public ExternalServiceResult(bool success)
        {
            this.Success = success;
        }

        public ExternalServiceResult(bool success, string message)
            : this(success)
        {
            this.Message = message;
        }

        public ExternalServiceResult(string message) : this(false, message) { }

        public ExternalServiceResult(Exception exception)
            : this(exception.Message)
        {
            this.Exception = exception;
        }
    }
}
