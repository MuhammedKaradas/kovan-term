namespace KovanTerm.Models;

/// <summary>Bir terminal/ajan düğümünün durumu. Gerçek değerler v0.2'de ajan hook'larından gelecek.</summary>
public enum NodeStatus
{
    Idle,
    Working,
    Waiting,
    Done,
    Error,
}
