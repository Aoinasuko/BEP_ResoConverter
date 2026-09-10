using System.Numerics;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux;

namespace nadena.dev.resonity.remote.puppeteer.dynamic_flux;

public class FluxBuilder : IDisposable
{
    private static readonly Vector2 DefaultBoundingBox = new Vector2(0.25f, 0.075f);
    
    private const float Padding = 0.05f;
    private Slot _packInto;
    private List<(Slot, Vector2)> _nodes = new();
    private Vector2 _direction = new Vector2(1, 0);
    private FluxBuilder? _parent = null;

    public FluxBuilder(Slot packInto)
    {
        this._packInto = packInto;
    }

    public FluxBuilder(Slot parent, string name)
        : this(parent.AddSlot(name))
    {
        
    }
    
    public void Dispose()
    {
        var totalWidth = _nodes.Select(n =>  Vector2.Dot(n.Item2, _direction)).Sum()
            + (_nodes.Count - 1) * Padding;
        var minorAxis = new Vector2(_direction.Y, _direction.X);
        var minorAxisWidth = _nodes.Select(n => Vector2.Dot(n.Item2, minorAxis)).Max();
        var cursor = -totalWidth / 2;

        for (int i = 0; i < _nodes.Count; i++)
        {
            _nodes[i].Item1.LocalPosition = (float2)(cursor * _direction);
            cursor += Vector2.Dot(_nodes[i].Item2, _direction) + Padding;
        }

        if (_parent != null)
        {
            var boundingBox = _direction * totalWidth + minorAxis * minorAxisWidth;
            
            _parent._nodes.Add((_packInto, boundingBox));
        }
    }

    private FluxBuilder(FluxBuilder parent, bool isHorizontal, string groupName)
    {
        _parent = parent;
        _packInto = _parent._packInto.AddSlot(groupName);
        _direction = isHorizontal ? new Vector2(1, 0) : new Vector2(0, -1);
    }

    public FluxBuilder Horizontal(string? groupName = null)
    {
        groupName ??= "HorizontalGroup";
        return new FluxBuilder(this, true, groupName);
    }

    public FluxBuilder Vertical(string? groupName = null)
    {
        groupName ??= "VerticalGroup";
        return new FluxBuilder(this, false, groupName);
    }

    public T Spawn<T>() where T : Component, IProtoFluxNode, new()
    {
        var tyName = typeof(T).ToString();
        var slot  = _packInto.AddSlot(tyName);
        
        // Assume an arbitrary bounding box for now
        _nodes.Add((slot, DefaultBoundingBox));

        return slot.AttachComponent<T>();
    }
}