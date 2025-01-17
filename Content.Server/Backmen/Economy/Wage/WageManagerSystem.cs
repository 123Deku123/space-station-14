﻿using Content.Shared.Backmen.Economy;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server.Backmen.Economy.Wage;

public sealed class WagePaydayEvent : EntityEventArgs
{
    public FixedPoint2 Mod { get; set; } = 1;
    public FixedPoint2? Value { get; set; } = null;
    public readonly HashSet<Entity<BankAccountComponent>> WhiteListFrom = new();
}

public sealed record WagePaydayPayout(Entity<BankAccountComponent> FromAccountNumber, Entity<BankAccountComponent> ToAccountNumber, FixedPoint2 PayoutAmount);

public sealed class WageManagerSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly BankManagerSystem _bankManagerSystem = default!;

    [ViewVariables(VVAccess.ReadWrite)]
    public readonly HashSet<WagePaydayPayout> PayoutsList = new();

    [ViewVariables(VVAccess.ReadWrite)]
    public bool WagesEnabled { get; private set; }
    //private void SetEnabled(bool value) => WagesEnabled = value;
    private void SetEnabled(bool value)
    {
        WagesEnabled = value;
    }
    public override void Initialize()
    {
        base.Initialize();
        _configurationManager.OnValueChanged(Shared.Backmen.CCVar.CCVars.EconomyWagesEnabled, SetEnabled, true);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
        SubscribeLocalEvent<WagePaydayEvent>(OnPayday);
    }

    private void OnCleanup(RoundRestartCleanupEvent ev)
    {
        PayoutsList.Clear();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _configurationManager.UnsubValueChanged(Shared.Backmen.CCVar.CCVars.EconomyWagesEnabled, SetEnabled);
    }

    public void OnPayday(WagePaydayEvent ev)
    {
        foreach (var payout in PayoutsList)
        {
            // бонусная зп на отдел?
            if (ev.WhiteListFrom.Count > 0 && !ev.WhiteListFrom.Contains(payout.FromAccountNumber))
            {
                continue;
            }
            var val = ev.Value ?? payout.PayoutAmount;

            _bankManagerSystem.TryTransferFromToBankAccount(
                payout.FromAccountNumber,
                payout.ToAccountNumber,
                val * ev.Mod);
        }
    }
    public bool TryAddAccountToWagePayoutList(Entity<BankAccountComponent> bankAccount, JobPrototype jobPrototype)
    {
        if (jobPrototype.WageDepartment == null || !_prototypeManager.TryIndex(jobPrototype.WageDepartment, out DepartmentPrototype? department))
            return false;

        if (!_bankManagerSystem.TryGetBankAccount(department.AccountNumber, out var departmentBankAccount))
            return false;

        var newPayout = new WagePaydayPayout(departmentBankAccount.Value, bankAccount, jobPrototype.Wage);
        PayoutsList.Add(newPayout);
        return true;
    }
}
