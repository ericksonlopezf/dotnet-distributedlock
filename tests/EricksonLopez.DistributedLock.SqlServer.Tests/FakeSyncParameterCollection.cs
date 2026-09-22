// Copyright © Erickson Lopez. MIT License.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.DistributedLock.SqlServer.Tests;

public sealed class FakeSyncParameterCollection : IDataParameterCollection
{
    private readonly List<IDbDataParameter> _list = new();

    public object this[string parameterName]
    {
        get => _list.Find(p => p.ParameterName == parameterName)!;
        set
        {
            var idx = IndexOf(parameterName);
            if (idx >= 0) _list[idx] = (IDbDataParameter)value;
            else _list.Add((IDbDataParameter)value);
        }
    }

    public object this[int index]
    {
        get => _list[index];
        set => _list[index] = (IDbDataParameter)value;
    }

    public int Count => _list.Count;
    public bool IsReadOnly => false;
    public bool IsFixedSize => false;
    public bool IsSynchronized => false;
    public object SyncRoot => ((ICollection)_list).SyncRoot;

    public int Add(object value)
    {
        _list.Add((IDbDataParameter)value);
        return _list.Count - 1;
    }

    public void Clear() => _list.Clear();
    public bool Contains(object value) => _list.Contains((IDbDataParameter)value);
    public bool Contains(string parameterName) => _list.Exists(p => p.ParameterName == parameterName);
    public void CopyTo(Array array, int index) => ((ICollection)_list).CopyTo(array, index);
    public IEnumerator GetEnumerator() => _list.GetEnumerator();
    public int IndexOf(object value) => _list.IndexOf((IDbDataParameter)value);
    public int IndexOf(string parameterName) => _list.FindIndex(p => p.ParameterName == parameterName);
    public void Insert(int index, object value) => _list.Insert(index, (IDbDataParameter)value);
    public void Remove(object value) => _list.Remove((IDbDataParameter)value);
    public void RemoveAt(int index) => _list.RemoveAt(index);
    public void RemoveAt(string parameterName)
    {
        var idx = IndexOf(parameterName);
        if (idx >= 0) _list.RemoveAt(idx);
    }
}

