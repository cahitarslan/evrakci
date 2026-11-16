namespace Evrakci.Core;

/// <summary>
/// An error whose message is safe to show to the user: it is written in Turkish and never
/// contains personal data (values, document text).
/// </summary>
public class EvrakciException(string message) : Exception(message);
