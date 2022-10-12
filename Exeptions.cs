using System;
using System.Runtime.Serialization;

namespace Syracuse;

[Serializable]
public class MailExсeption : Exception
{
    public MailExсeption() { }
    public MailExсeption(string message) : base(message) { }
    public MailExсeption(string message, Exception innerException) : base(message, innerException) { }
    public MailExсeption(SerializationInfo info, StreamingContext context) : base(info, context) { }
}

[Serializable]
public class PdfExсeption : Exception
{
    public PdfExсeption() { }
    public PdfExсeption(string message) : base(message) { }
    public PdfExсeption(string message, Exception innerException) : base(message, innerException) { }
    public PdfExсeption(SerializationInfo info, StreamingContext context) : base(info, context) { }
}

[Serializable]
public class DbExсeption : Exception
{
    public DbExсeption() { }
    public DbExсeption(string message) : base(message) { }
    public DbExсeption(string message, Exception innerException) : base(message, innerException) { }
    public DbExсeption(SerializationInfo info, StreamingContext context) : base(info, context) { }
}

[Serializable]
public class CustomerExсeption : Exception
{
    public CustomerExсeption() { }
    public CustomerExсeption(string message) : base(message) { }
    public CustomerExсeption(string message, Exception innerException) : base(message, innerException) { }
    public CustomerExсeption(SerializationInfo info, StreamingContext context) : base(info, context) { }
}
