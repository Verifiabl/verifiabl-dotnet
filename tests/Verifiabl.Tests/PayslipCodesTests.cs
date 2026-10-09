using System.Reflection;
using System.Text.Json;
using Verifiabl.Client;
using Xunit;

namespace Verifiabl.Tests;

public class PayslipCodesTests
{
    public static TheoryData<Type> CodeSets() =>
    [
        typeof(AustralianEarningsTypes),
        typeof(AustralianPaidLeaveTypes),
        typeof(AustralianAllowanceTypes),
        typeof(AustralianOtherAllowanceCategories),
        typeof(AustralianLumpSumTypes),
        typeof(AustralianEtpTypes),
        typeof(AustralianEtpComponents),
        typeof(AustralianSalarySacrificeTypes),
        typeof(AustralianDeductionTypes),
        typeof(AustralianSuperContributionTypes),
        typeof(AustralianPayFrequencies),
        typeof(AustralianEmploymentBases),
        typeof(AustralianEngagementTypes),
        typeof(NewZealandEarningsTypes),
        typeof(NewZealandPaidLeaveTypes),
        typeof(NewZealandAllowanceTypes),
        typeof(NewZealandDeductionTypes),
        typeof(NewZealandLeaveBalanceUnits),
    ];

    [Theory]
    [MemberData(nameof(CodeSets))]
    public void AllListsEveryConstantOnce(Type codeSet)
    {
        // Reflection does not guarantee member order, so compare as sets.
        HashSet<string> constants = codeSet.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);
        var all = (IReadOnlyList<string>)codeSet.GetField("All")!.GetValue(null)!;

        Assert.NotEmpty(constants);
        Assert.Equal(all.Count, constants.Count);
        Assert.True(constants.SetEquals(all));
        Assert.All(all, code => Assert.Matches("\\A[a-z]+(?:_[a-z]+)*\\z", code));
    }

    [Fact]
    public void EarningsTypesCoverEveryFactory()
    {
        Assert.True(FactoryTypes<AustralianPayslipV2EarningsItem>().SetEquals(AustralianEarningsTypes.All));
        Assert.True(FactoryTypes<NewZealandPayslipV2EarningsItem>().SetEquals(NewZealandEarningsTypes.All));
    }

    [Fact]
    public void FactoriesSetOnlyTheirVariantFields()
    {
        AustralianPayslipV2EarningsItem leave = AustralianPayslipV2EarningsItem.PaidLeave(
            AustralianPaidLeaveTypes.PaidParental, 1520.00m, units: 38m, rate: 40.00m);
        AustralianPayslipV2EarningsItem allowance = AustralianPayslipV2EarningsItem.Allowance(
            AustralianAllowanceTypes.Tools, 20m);
        AustralianPayslipV2EarningsItem other = AustralianPayslipV2EarningsItem.OtherAllowance(
            AustralianOtherAllowanceCategories.HomeOffice, 12.50m);
        NewZealandPayslipV2EarningsItem overtime = NewZealandPayslipV2EarningsItem.Overtime(300m, ytdAmount: 1200m);

        Assert.Equal(
            "{\"type\":\"paid_leave\",\"leave_type\":\"paid_parental\",\"amount\":\"1520.00\",\"units\":\"38\",\"rate\":\"40.00\"}",
            Serialize(leave));
        Assert.Equal("{\"type\":\"allowance\",\"amount\":\"20\",\"allowance_type\":\"tools\"}", Serialize(allowance));
        Assert.Equal(
            "{\"type\":\"allowance\",\"amount\":\"12.50\",\"allowance_type\":\"other\",\"other_category\":\"home_office\"}",
            Serialize(other));
        Assert.Equal("{\"type\":\"overtime\",\"amount\":\"300\",\"ytd_amount\":\"1200\"}", Serialize(overtime));
    }

    [Fact]
    public void LumpSumAndEtpFactoriesSetTheirRequiredType()
    {
        AustralianPayslipV2EarningsItem lumpSum = AustralianPayslipV2EarningsItem.LumpSum(
            AustralianLumpSumTypes.ARedundancy, 6000.00m, ytdAmount: 6000.00m);
        AustralianPayslipV2EarningsItem etp = AustralianPayslipV2EarningsItem.Etp(
            AustralianEtpTypes.DeathNonDependantSplit, AustralianEtpComponents.TaxFree, 12000.00m, units: 8m, rate: 1500.00m);

        Assert.Equal(
            "{\"type\":\"lump_sum\",\"amount\":\"6000.00\",\"ytd_amount\":\"6000.00\",\"lump_sum_type\":\"a_redundancy\"}",
            Serialize(lumpSum));
        Assert.Equal(
            "{\"type\":\"etp\",\"amount\":\"12000.00\",\"units\":\"8\",\"rate\":\"1500.00\",\"etp_type\":\"death_non_dependant_split\",\"etp_component\":\"tax_free\"}",
            Serialize(etp));
        Assert.Equal(new[] { "lumpSumType", "amount", "units?", "rate?", "ytdAmount?" }, FactoryParameters("LumpSum"));
        Assert.Equal(new[] { "etpType", "etpComponent", "amount", "units?", "rate?", "ytdAmount?" }, FactoryParameters("Etp"));
        Assert.Equal(new[] { "a_redundancy", "a_other", "b", "d", "e" }, AustralianLumpSumTypes.All);
        Assert.Equal(
            new[]
            {
                "redundancy", "other", "redundancy_split", "other_split", "death_dependant", "death_non_dependant",
                "death_non_dependant_split", "death_trustee",
            },
            AustralianEtpTypes.All);
        Assert.Equal(new[] { "taxable", "tax_free" }, AustralianEtpComponents.All);
    }

    [Fact]
    public void AllowanceFactoryRejectsTheOtherTypeThatNeedsACategory()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => AustralianPayslipV2EarningsItem.Allowance(AustralianAllowanceTypes.Other, 12.50m));

        Assert.Equal("allowanceType", exception.ParamName);
        Assert.Contains("OtherAllowance", exception.Message);
    }

    // Names the parameters, marking each optional one with a trailing "?".
    private static IEnumerable<string> FactoryParameters(string name) => typeof(AustralianPayslipV2EarningsItem)
        .GetMethod(name)!
        .GetParameters()
        .Select(parameter => parameter.Name + (parameter.HasDefaultValue ? "?" : ""));

    private static HashSet<string> FactoryTypes<T>() => typeof(T)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(method => method.ReturnType == typeof(T))
        .Select(method => (string)typeof(T).GetProperty("Type")!.GetValue(method.Invoke(null, FactoryArguments(method)))!)
        .ToHashSet(StringComparer.Ordinal);

    private static object?[] FactoryArguments(MethodInfo method) => method.GetParameters()
        .Select(parameter => parameter.HasDefaultValue ? null : parameter.ParameterType == typeof(decimal) ? (object)1m : "code")
        .ToArray();

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    });
}
