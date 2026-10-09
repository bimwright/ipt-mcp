using System;

namespace Bimwright.Ipt.Shared.Handlers.ILogic;

internal interface ILogicResultStore
{
    bool TryRead(out object? value);
    void Write(object value);
    void Delete();
}

/// <summary>Owns one invocation's sentinel, preserving an existing string property and its value.</summary>
internal sealed class ILogicResultChannel
{
    internal const int MaxChars = 65536;
    private readonly ILogicResultStore _store;
    private readonly string _sentinel = "MCP_PENDING_" + Guid.NewGuid().ToString("N");
    private bool _started, _existed;
    private object? _previous;

    internal ILogicResultChannel(ILogicResultStore store) => _store = store;

    internal void Begin()
    {
        _existed = _store.TryRead(out _previous);
        if (_existed && _previous is not string)
            throw new ArgumentException("MCP_RESULT already exists with a non-string value; the snippet result channel cannot safely use it.");
        _started = true; // Restore also covers a setter that mutates before throwing.
        _store.Write(_sentinel);
    }

    internal string? Read()
    {
        if (!_store.TryRead(out var value)) return null;
        var text = value?.ToString();
        if (text == _sentinel || string.IsNullOrEmpty(text)) return null;
        if (text!.Length > MaxChars)
            throw new InvalidOperationException("iLogic result exceeds 65536 characters; return a smaller result. The result was not truncated.");
        return text;
    }

    internal void Restore()
    {
        if (!_started) return;
        if (_existed)
        {
            _store.Write(_previous!);
            if (!_store.TryRead(out var restored) || !Equals(restored, _previous))
                throw new InvalidOperationException("The original MCP_RESULT value could not be restored.");
        }
        else
        {
            if (_store.TryRead(out _)) _store.Delete();
            if (_store.TryRead(out _)) throw new InvalidOperationException("The invocation's MCP_RESULT property remains after cleanup.");
        }
        _started = false;
    }
}
