using Content.Server.GameTicking.Rules.Components;
using Content.Server.StationEvents.Components;
using Content.Server.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.StationEvents.Events;

public sealed class RandomEntityStorageSpawnRule : StationEventSystem<RandomEntityStorageSpawnRuleComponent>
{
    [Dependency] private readonly EntityStorageSystem _entityStorage = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    protected override void Started(EntityUid uid, RandomEntityStorageSpawnRuleComponent comp, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, comp, gameRule, args);

        if (!TryGetRandomStation(out var station))
            return;

        var spawn = Spawn(comp.Prototype, MapCoordinates.Nullspace);
        var validLockers = new List<(EntityUid, EntityStorageComponent, TransformComponent)>();

        var query = EntityQueryEnumerator<EntityStorageComponent, TransformComponent>();
        while (query.MoveNext(out var ent, out var storage, out var xform))
        {
            if (StationSystem.GetOwningStation(ent, xform) != station)
                continue;

            if (!_entityStorage.CanInsert(spawn, ent, storage))
                continue;

            validLockers.Add((ent, storage, xform));
        }

        if (validLockers.Count == 0)
        {
            Del(spawn);
            return;
        }

        var (locker, storageComp, lockerPos) = RobustRandom.Pick(validLockers);
        _transform.SetCoordinates(spawn, lockerPos.Coordinates);
        if (!_entityStorage.Insert(spawn, locker, storageComp))
        {
            Del(spawn);
        }
    }
}
