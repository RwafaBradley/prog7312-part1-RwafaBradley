using System.Collections;
using SmartX.Core.Domain;

namespace SmartX.Core.Collections;

// a dictionary for finding a node fast, because every single arriving packet does one of these lookups
// and a list beside it to hold the order steady, a table that reshuffles every second is one you lose your place in
public sealed class SensorRegistry : IReadOnlyCollection<SensorRegistration>
{
    private readonly Dictionary<string, SensorRegistration> _byMac =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<SensorRegistration> _ordered = new();

    // kept per category so a filtered view never has to walk the whole register
    private readonly Dictionary<SensorCategory, List<SensorRegistration>> _byCategory = new();

    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try { return _ordered.Count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    public bool Register(SensorRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        _lock.EnterWriteLock();
        try
        {
            if (_byMac.TryGetValue(registration.MacAddress, out var existing))
            {
                registration.RegisteredUtc = existing.RegisteredUtc;

                if (registration.Attachments.Count == 0 && existing.Attachments.Count > 0)
                {
                    registration.Attachments = existing.Attachments;
                }

                var slot = _ordered.IndexOf(existing);
                if (slot >= 0)
                {
                    _ordered[slot] = registration;
                }

                RemoveFromCategoryIndex(existing);
                _byMac[registration.MacAddress] = registration;
                AddToCategoryIndex(registration);
                return false;
            }

            _byMac[registration.MacAddress] = registration;
            _ordered.Add(registration);
            AddToCategoryIndex(registration);
            return true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool TryGet(string macAddress, out SensorRegistration registration)
    {
        _lock.EnterReadLock();
        try
        {
            return _byMac.TryGetValue(macAddress ?? string.Empty, out registration!);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool Contains(string macAddress)
    {
        _lock.EnterReadLock();
        try { return _byMac.ContainsKey(macAddress ?? string.Empty); }
        finally { _lock.ExitReadLock(); }
    }

    public IReadOnlyList<SensorRegistration> InCategory(SensorCategory category)
    {
        _lock.EnterReadLock();
        try
        {
            return _byCategory.TryGetValue(category, out var list)
                ? list.ToArray()
                : Array.Empty<SensorRegistration>();
        }
        finally { _lock.ExitReadLock(); }
    }

    public IReadOnlyList<SensorRegistration> Snapshot()
    {
        _lock.EnterReadLock();
        try { return _ordered.ToArray(); }
        finally { _lock.ExitReadLock(); }
    }

    public bool Remove(string macAddress)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_byMac.TryGetValue(macAddress ?? string.Empty, out var existing))
            {
                return false;
            }

            _byMac.Remove(existing.MacAddress);
            _ordered.Remove(existing);
            RemoveFromCategoryIndex(existing);
            return true;
        }
        finally { _lock.ExitWriteLock(); }
    }

    private void AddToCategoryIndex(SensorRegistration registration)
    {
        if (!_byCategory.TryGetValue(registration.Category, out var list))
        {
            list = new List<SensorRegistration>();
            _byCategory[registration.Category] = list;
        }

        list.Add(registration);
    }

    private void RemoveFromCategoryIndex(SensorRegistration registration)
    {
        if (_byCategory.TryGetValue(registration.Category, out var list))
        {
            list.Remove(registration);
        }
    }

    public IEnumerator<SensorRegistration> GetEnumerator()
        => ((IEnumerable<SensorRegistration>)Snapshot()).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
