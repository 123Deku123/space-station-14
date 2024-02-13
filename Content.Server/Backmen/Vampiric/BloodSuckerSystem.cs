using Content.Shared.Verbs;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Administration.Logs;
using Content.Shared.Backmen.Vampiric;
using Content.Server.Atmos.Components;
using Content.Server.Backmen.Vampiric.Role;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Server.Popups;
using Content.Server.HealthExaminable;
using Content.Server.DoAfter;
using Content.Server.Mind;
using Content.Server.Nutrition.Components;
using Content.Server.Nutrition.EntitySystems;
using Content.Server.Objectives;
using Content.Server.Polymorph.Components;
using Content.Server.Polymorph.Systems;
using Content.Server.Roles;
using Content.Server.Store.Components;
using Content.Server.Store.Systems;
using Content.Shared.Actions;
using Content.Shared.Backmen.Spider.Components;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Objectives.Components;
using Content.Shared.Polymorph;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Store;
using Content.Shared.Stunnable;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server.Backmen.Vampiric;

public sealed class BloodSuckerSystem : SharedBloodSuckerSystem
{
    [Dependency] private readonly BodySystem _bodySystem = default!;
    [Dependency] private readonly SolutionContainerSystem _solutionSystem = default!;
    [Dependency] private readonly PopupSystem _popups = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly StomachSystem _stomachSystem = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly InventorySystem _inventorySystem = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private readonly ReactiveSystem _reactiveSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly HungerSystem _hunger = default!;
    [Dependency] private readonly RoleSystem _roleSystem = default!;
    [Dependency] private readonly MindSystem _mindSystem = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ISharedPlayerManager _playerManager = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
    [Dependency] private readonly BkmVampireLevelingSystem _leveling = default!;


    [ValidatePrototypeId<AntagPrototype>] private const string BloodsuckerAntagRole = "Bloodsucker";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BloodSuckerComponent, GetVerbsEvent<InnateVerb>>(AddSuccVerb);
        SubscribeLocalEvent<BloodSuckedComponent, HealthBeingExaminedEvent>(OnHealthExamined);
        SubscribeLocalEvent<BloodSuckedComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<BloodSuckerComponent, BloodSuckDoAfterEvent>(OnDoAfter);


        SubscribeLocalEvent<BkmVampireComponent, MapInitEvent>(OnInitVmp);

        SubscribeLocalEvent<BkmVampireComponent, PlayerAttachedEvent>(OnAttachedVampireMind);
        SubscribeLocalEvent<BkmVampireComponent, HealthBeingExaminedEvent>(OnVampireExamined);
        SubscribeLocalEvent<BkmVampireComponent, InnateNewVampierActionEvent>(OnUseNewVamp);
        SubscribeLocalEvent<BkmVampireComponent, InnateNewVampierDoAfterEvent>(OnUseNewVampAfter);

        SubscribeLocalEvent<BloodSuckerComponent, PolymorphActionEvent>(OnPolymorphActionEvent, before: new []{ typeof(PolymorphSystem) });

        SubscribeLocalEvent<BloodsuckerConvertConditionComponent, ObjectiveGetProgressEvent>(OnGetConvertProgress);
        SubscribeLocalEvent<BloodsuckerDrinkConditionComponent, ObjectiveGetProgressEvent>(OnGetDrinkProgress);
        SubscribeLocalEvent<BloodsuckerConvertConditionComponent, ObjectiveAssignedEvent>(OnConvertAssigned);
        SubscribeLocalEvent<BloodsuckerConvertConditionComponent, ObjectiveAfterAssignEvent>(OnConvertAfterAssigned);
        SubscribeLocalEvent<BloodsuckerDrinkConditionComponent, ObjectiveAssignedEvent>(OnDrinkAssigned);
        SubscribeLocalEvent<BloodsuckerDrinkConditionComponent, ObjectiveAfterAssignEvent>(OnDrinkAfterAssigned);
    }





    private void OnUseNewVampAfter(Entity<BkmVampireComponent> ent, ref InnateNewVampierDoAfterEvent args)
    {
        if (args.Cancelled || args.Target == null || TerminatingOrDeleted(args.Target.Value))
        {
            _actions.ClearCooldown(ent.Comp.ActionNewVamp);
            return;
        }

        if (_mindSystem.TryGetMind(ent, out var entMindId, out _))
        {
            EnsureComp<BloodSuckedComponent>(args.Target.Value).BloodSuckerMindId = entMindId;
        }

        // Add a little pierce
        DamageSpecifier damage = new();
        damage.DamageDict.Add("Piercing", 1); // Slowly accumulate enough to gib after like half an hour

        _damageableSystem.TryChangeDamage(args.Target.Value, damage, true, true);

        ConvertToVampire(args.Target.Value);
        _stun.TryKnockdown(args.Target.Value, TimeSpan.FromSeconds(30), true);
        _stun.TryParalyze(args.Target.Value, TimeSpan.FromSeconds(30), true);

        _hunger.ModifyHunger(ent, -100);
        _stun.TryStun(ent, TimeSpan.FromSeconds(_random.Next(1, 3)), true);
    }

    private void OnUseNewVamp(Entity<BkmVampireComponent> ent, ref InnateNewVampierActionEvent args)
    {
        if (HasComp<BkmVampireComponent>(args.Target))
        {
            return;
        }

        if (_mobStateSystem.IsDead(args.Target))
        {
            return;
        }

        if (!TryComp<BloodstreamComponent>(args.Target, out var bloodstream))
            return;

        if (bloodstream.BloodReagent != "Blood" || bloodstream.BloodSolution == null)
        {
            _popups.PopupEntity(Loc.GetString("bloodsucker-fail-not-blood", ("target", args.Target)), args.Target, ent.Owner, Shared.Popups.PopupType.Medium);
            return;
        }

        if (TryComp<HungerComponent>(ent, out var hunger) && _hunger.GetHungerThreshold(hunger) < HungerThreshold.Okay)
        {
            _popupSystem.PopupEntity("Вы хотите есть", ent, ent);
            return;
        }

        if (TryComp<ThirstComponent>(ent, out var thirst) && thirst.CurrentThirstThreshold < ThirstThreshold.Okay)
        {
            _popupSystem.PopupEntity("Вы хотите пить", ent, ent);
            return;
        }

        _popups.PopupEntity(Loc.GetString("bloodsucker-doafter-start-victim", ("sucker", ent.Owner)), args.Target, args.Target, Shared.Popups.PopupType.LargeCaution);
        _popups.PopupEntity(Loc.GetString("bloodsucker-doafter-start", ("target", args.Target)), args.Target, ent, Shared.Popups.PopupType.Medium);

        _doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, ent, TimeSpan.FromSeconds(10),
            new InnateNewVampierDoAfterEvent(), ent, target: args.Target, used: ent)
        {
            BreakOnUserMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            RequireCanInteract = true,
            BreakOnHandChange = true,
            BreakOnTargetMove = true,
            BreakOnWeightlessMove = true
        });

        _audio.PlayPvs("/Audio/Items/drink.ogg", ent,
            AudioParams.Default.WithVariation(0.025f));
        args.Handled = true;
    }

    private void OnInitVmp(Entity<BkmVampireComponent> ent, ref MapInitEvent args)
    {
        _leveling.InitShop(ent);
    }

    private void OnVampireExamined(Entity<BkmVampireComponent> ent, ref HealthBeingExaminedEvent args)
    {
        if (!_hunger.IsHungerBelowState(ent, HungerThreshold.Okay))
            return;

        args.Message.PushNewline();
        args.Message.AddMarkup(Loc.GetString("vampire-health-examine", ("target", ent.Owner)));
    }

    private void OnDrinkAfterAssigned(Entity<BloodsuckerDrinkConditionComponent> condition, ref ObjectiveAfterAssignEvent args)
    {
        _metaData.SetEntityName(condition.Owner, Loc.GetString(condition.Comp.ObjectiveText, ("goal", condition.Comp.Goal)), args.Meta);
        _metaData.SetEntityDescription(condition.Owner, Loc.GetString(condition.Comp.DescriptionText, ("goal", condition.Comp.Goal)), args.Meta);
    }

    private void OnConvertAfterAssigned(Entity<BloodsuckerConvertConditionComponent> condition, ref ObjectiveAfterAssignEvent args)
    {
        _metaData.SetEntityName(condition.Owner, Loc.GetString(condition.Comp.ObjectiveText, ("goal", condition.Comp.Goal)), args.Meta);
        _metaData.SetEntityDescription(condition.Owner, Loc.GetString(condition.Comp.DescriptionText, ("goal", condition.Comp.Goal)), args.Meta);
    }

    private void OnConvertAssigned(Entity<BloodsuckerConvertConditionComponent> ent, ref ObjectiveAssignedEvent args)
    {
        if (args.Mind.OwnedEntity == null)
        {
            args.Cancelled = true;
            return;
        }

        var user = args.Mind.OwnedEntity.Value;
        if (!TryComp<BkmVampireComponent>(user, out var vmp))
        {
            args.Cancelled = true;
            return;
        }

        ent.Comp.Goal = _random.Next(
            1,
            Math.Max(1, // min 1 of 1
                Math.Min(
                    ent.Comp.MaxGoal, // 5
                    (int)Math.Ceiling(Math.Max(_playerManager.PlayerCount, 1f) / ent.Comp.PerPlayers) // per players with max
                    )
                )
            );
    }

    private void OnGetConvertProgress(Entity<BloodsuckerConvertConditionComponent> ent, ref ObjectiveGetProgressEvent args)
    {
        if (args.Mind.OwnedEntity == null || !TryComp<VampireRoleComponent>(args.MindId, out var vmp))
        {
            args.Progress = 0;
            return;
        }

        args.Progress = vmp.Converted / ent.Comp.Goal;
    }

    private void OnDrinkAssigned(Entity<BloodsuckerDrinkConditionComponent> ent, ref ObjectiveAssignedEvent args)
    {
        if (args.Mind.OwnedEntity == null)
        {
            args.Cancelled = true;
            return;
        }

        var user = args.Mind.OwnedEntity.Value;
        if (!TryComp<BkmVampireComponent>(user, out var vmp))
        {
            args.Cancelled = true;
            return;
        }

        ent.Comp.Goal = _random.Next(
            ent.Comp.MinGoal,
            Math.Max(ent.Comp.MinGoal + 1, // min 1 of 1
                ent.Comp.MaxGoal
            )
        );
    }

    private void OnGetDrinkProgress(Entity<BloodsuckerDrinkConditionComponent> ent, ref ObjectiveGetProgressEvent args)
    {
        if (args.Mind.OwnedEntity == null || !TryComp<VampireRoleComponent>(args.MindId, out var vmp))
        {
            args.Progress = 0;
            return;
        }

        args.Progress = vmp.Drink / ent.Comp.Goal;
    }

    private void OnAttachedVampireMind(Entity<BkmVampireComponent> ent, ref PlayerAttachedEvent args)
    {
        EnsureMindVampire(ent);
    }

    private void OnPolymorphActionEvent(Entity<BloodSuckerComponent> ent, ref PolymorphActionEvent args)
    {
        if(TryComp<HungerComponent>(ent, out var hungerComponent))
            _hunger.ModifyHunger(ent, -30, hungerComponent);
    }

    [ValidatePrototypeId<EntityPrototype>]
    private const string OrganVampiricHumanoidStomach = "OrganVampiricHumanoidStomach";

    [ValidatePrototypeId<ReagentPrototype>]
    private const string BloodSuckerToxin = "BloodSuckerToxin";

    [ValidatePrototypeId<EntityPrototype>]
    private const string EscapeObjective = "EscapeShuttleObjectiveBloodsucker";

    [ValidatePrototypeId<EntityPrototype>]
    private const string Objective1 = "BloodsuckerDrinkObjective";

    [ValidatePrototypeId<EntityPrototype>]
    private const string Objective2 = "BloodsuckerConvertObjective";

    public void ConvertToVampire(EntityUid uid)
    {
        if (
            HasComp<BloodSuckerComponent>(uid) ||
            !TryComp<BodyComponent>(uid, out var bodyComponent) ||
            !TryComp<BodyPartComponent>(bodyComponent.RootContainer.ContainedEntity, out var bodyPartComponent)
            )
            return;

        var bloodSucker = EnsureComp<BloodSuckerComponent>(uid);
        //bloodSucker.InjectReagent = BloodSuckerToxin;
        //bloodSucker.UnitsToInject = 10;
        //bloodSucker.InjectWhenSucc = true;

        {
            var stomachs = _bodySystem.GetBodyOrganComponents<StomachComponent>(uid);
            foreach (var (comp, organ) in stomachs)
            {
                _bodySystem.RemoveOrgan(organ.Owner, organ);
                QueueDel(organ.Owner);
            }
        }

        var stomach = Spawn(OrganVampiricHumanoidStomach);

        _bodySystem.InsertOrgan(bodyComponent.RootContainer.ContainedEntity.Value, stomach, "stomach", bodyPartComponent);

        EnsureComp<BkmVampireComponent>(uid);

        if (
            TryComp<BloodSuckedComponent>(uid, out var bloodsucked) &&
            bloodsucked.BloodSuckerMindId.HasValue &&
            !TerminatingOrDeleted(bloodsucked.BloodSuckerMindId.Value) &&
            TryComp<VampireRoleComponent>(bloodsucked.BloodSuckerMindId.Value, out var bloodsucker)
            )
        {
            var masterUid = CompOrNull<MindComponent>(bloodsucked.BloodSuckerMindId.Value)?.CurrentEntity;
            if (TryComp<BkmVampireComponent>(masterUid, out var master))
            {
                _leveling.AddCurrency((masterUid.Value,master), 10 * (bloodsucker.Tier + 1));
            }


            bloodsucker.Converted += 1;
        }

        EnsureMindVampire(uid);
    }

    public void EnsureMindVampire(EntityUid uid)
    {
        if (!_mindSystem.TryGetMind(uid, out var mindId, out var mind))
        {
            return; // no mind? skip;
        }

        if (_roleSystem.MindHasRole<VampireRoleComponent>(mindId))
        {
            return; // have it
        }

        _roleSystem.MindAddRole(mindId, new VampireRoleComponent()
        {
            PrototypeId = BloodsuckerAntagRole
        }, mind, true);

        _mindSystem.TryAddObjective(mindId, mind, EscapeObjective);
        _mindSystem.TryAddObjective(mindId, mind, Objective1);
        _mindSystem.TryAddObjective(mindId, mind, Objective2);
    }

    private void AddSuccVerb(EntityUid uid, BloodSuckerComponent component, GetVerbsEvent<InnateVerb> args)
    {
        if (args.User == args.Target)
            return;
        if (component.WebRequired)
            return; // handled elsewhere
        if (!TryComp<BloodstreamComponent>(args.Target, out var bloodstream))
            return;
        if (!args.CanAccess)
            return;

        InnateVerb verb = new()
        {
            Act = () =>
            {
                StartSuccDoAfter(uid, args.Target, component, bloodstream); // start doafter
            },
            Text = Loc.GetString("action-name-suck-blood"),
            Icon = new SpriteSpecifier.Texture(new ("/Textures/Backmen/Icons/verbiconfangs.png")),
            Priority = 2
        };
        args.Verbs.Add(verb);
    }

    private void OnHealthExamined(EntityUid uid, BloodSuckedComponent component, HealthBeingExaminedEvent args)
    {
        args.Message.PushNewline();
        args.Message.AddMarkup(Loc.GetString("bloodsucked-health-examine", ("target", uid)));
    }

    private void OnDamageChanged(EntityUid uid, BloodSuckedComponent component, DamageChangedEvent args)
    {
        if (args.DamageIncreased)
            return;

        if (_prototypeManager.TryIndex<DamageGroupPrototype>("Brute", out var brute) && args.Damageable.Damage.TryGetDamageInGroup(brute, out var bruteTotal)
            && _prototypeManager.TryIndex<DamageGroupPrototype>("Airloss", out var airloss) && args.Damageable.Damage.TryGetDamageInGroup(airloss, out var airlossTotal))
        {
            if (bruteTotal == 0 && airlossTotal == 0)
                RemComp<BloodSuckedComponent>(uid);
        }
    }

    private void OnDoAfter(EntityUid uid, BloodSuckerComponent component, BloodSuckDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Args.Target == null)
            return;

        args.Handled = TrySucc(uid, args.Args.Target.Value);
    }

    public void StartSuccDoAfter(EntityUid bloodsucker, EntityUid victim, BloodSuckerComponent? bloodSuckerComponent = null, BloodstreamComponent? stream = null, bool doChecks = true)
    {
        if (!Resolve(bloodsucker, ref bloodSuckerComponent))
            return;

        if (!Resolve(victim, ref stream))
            return;

        if (doChecks)
        {
            if (!_interactionSystem.InRangeUnobstructed(bloodsucker, victim))
            {
                return;
            }

            if (_inventorySystem.TryGetSlotEntity(victim, "head", out var headUid) && HasComp<PressureProtectionComponent>(headUid))
            {
                _popups.PopupEntity(Loc.GetString("bloodsucker-fail-helmet", ("helmet", headUid)), victim, bloodsucker, Shared.Popups.PopupType.Medium);
                return;
            }

            if (_inventorySystem.TryGetSlotEntity(bloodsucker, "mask", out var maskUid) &&
                EntityManager.TryGetComponent<IngestionBlockerComponent>(maskUid, out var blocker) &&
                blocker.Enabled)
            {
                _popups.PopupEntity(Loc.GetString("bloodsucker-fail-mask", ("mask", maskUid)), victim, bloodsucker, Shared.Popups.PopupType.Medium);
                return;
            }
        }

        if (stream.BloodReagent != "Blood" || stream.BloodSolution == null)
        {
            _popups.PopupEntity(Loc.GetString("bloodsucker-fail-not-blood", ("target", victim)), victim, bloodsucker, Shared.Popups.PopupType.Medium);
            return;
        }

        if (stream.BloodSolution.Value.Comp.Solution.Volume <= 1)
        {
            if (HasComp<BloodSuckedComponent>(victim))
                _popups.PopupEntity(Loc.GetString("bloodsucker-fail-no-blood-bloodsucked", ("target", victim)), victim, bloodsucker, Shared.Popups.PopupType.Medium);
            else
                _popups.PopupEntity(Loc.GetString("bloodsucker-fail-no-blood", ("target", victim)), victim, bloodsucker, Shared.Popups.PopupType.Medium);

            return;
        }

        _popups.PopupEntity(Loc.GetString("bloodsucker-doafter-start-victim", ("sucker", bloodsucker)), victim, victim, Shared.Popups.PopupType.LargeCaution);
        _popups.PopupEntity(Loc.GetString("bloodsucker-doafter-start", ("target", victim)), victim, bloodsucker, Shared.Popups.PopupType.Medium);

        var ev = new BloodSuckDoAfterEvent();
        var args = new DoAfterArgs(EntityManager, bloodsucker, bloodSuckerComponent.SuccDelay, ev, bloodsucker, target: victim)
        {
            BreakOnTargetMove = true,
            BreakOnUserMove = false,
            DistanceThreshold = 2f,
            NeedHand = false
        };

        _doAfter.TryStartDoAfter(args);
    }

    public bool TrySucc(EntityUid bloodsucker, EntityUid victim, BloodSuckerComponent? bloodsuckerComp = null, BloodstreamComponent? bloodstream = null)
    {
        // Is bloodsucker a bloodsucker?
        if (!Resolve(bloodsucker, ref bloodsuckerComp))
            return false;

        // Does victim have a bloodstream?
        if (!Resolve(victim, ref bloodstream))
            return false;

        // No blood left, yikes.
        if (bloodstream.BloodSolution == null || bloodstream.BloodSolution.Value.Comp.Solution.Volume == 0)
            return false;

        // Does bloodsucker have a stomach?
        var stomachList = _bodySystem.GetBodyOrganComponents<StomachComponent>(bloodsucker).FirstOrNull();
        if (stomachList == null)
            return false;

        if (!_solutionSystem.TryGetSolution(stomachList.Value.Comp.Owner, StomachSystem.DefaultSolutionName, out var stomachSolution))
            return false;

        // Are we too full?
        var unitsToDrain = bloodsuckerComp.UnitsToSucc;

        var stomachAvailableVolume = stomachSolution.Value.Comp.Solution.AvailableVolume;

        if (stomachAvailableVolume < unitsToDrain)
            unitsToDrain = (float) stomachAvailableVolume;

        if (unitsToDrain <= 2)
        {
            _popups.PopupEntity(Loc.GetString("drink-component-try-use-drink-had-enough"), bloodsucker, bloodsucker, Shared.Popups.PopupType.MediumCaution);
            return false;
        }

        _adminLogger.Add(Shared.Database.LogType.MeleeHit, Shared.Database.LogImpact.Medium, $"{ToPrettyString(bloodsucker):player} sucked blood from {ToPrettyString(victim):target}");

        // All good, succ time.
        _audio.PlayPvs("/Audio/Items/drink.ogg", bloodsucker);
        _popups.PopupEntity(Loc.GetString("bloodsucker-blood-sucked-victim", ("sucker", bloodsucker)), victim, victim, Shared.Popups.PopupType.LargeCaution);
        _popups.PopupEntity(Loc.GetString("bloodsucker-blood-sucked", ("target", victim)), bloodsucker, bloodsucker, Shared.Popups.PopupType.Medium);

        if (_mindSystem.TryGetMind(bloodsucker, out var bloodsuckermidId, out _))
        {
            EnsureComp<BloodSuckedComponent>(victim).BloodSuckerMindId = bloodsuckermidId;
            if (TryComp<VampireRoleComponent>(bloodsuckermidId, out var vpm))
            {
                vpm.Drink += unitsToDrain;

                if (TryComp<BkmVampireComponent>(bloodsucker, out var bkmVampireComponent))
                {
                    _leveling.AddCurrency((bloodsucker,bkmVampireComponent), 1 * (vpm.Tier + 1));
                }
            }
        }
        else
        {
            EnsureComp<BloodSuckedComponent>(victim).BloodSuckerMindId = null;
        }




        var bloodSolution = bloodstream.BloodSolution.Value;
        // Make everything actually ingest.
        var temp = _solutionSystem.SplitSolution(bloodSolution, unitsToDrain);
        _reactiveSystem.DoEntityReaction(bloodsucker, temp, Shared.Chemistry.Reagent.ReactionMethod.Ingestion);
        _stomachSystem.TryTransferSolution(stomachList.Value.Comp.Owner, temp, stomachList.Value.Comp);

        // Add a little pierce
        DamageSpecifier damage = new();
        damage.DamageDict.Add("Piercing", 1); // Slowly accumulate enough to gib after like half an hour

        _damageableSystem.TryChangeDamage(victim, damage, true, true);

        if (bloodsuckerComp.InjectWhenSucc && _solutionSystem.TryGetSolution(victim, bloodstream.ChemicalSolutionName, out var chemical))
        {
            _solutionSystem.TryAddReagent(chemical.Value, bloodsuckerComp.InjectReagent, bloodsuckerComp.UnitsToInject, out var acceptedQuantity);
        }


        return true;
    }
}
