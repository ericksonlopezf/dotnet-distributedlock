// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.DistributedLock.Abstractions;
using FluentAssertions;
using Xunit;

namespace EricksonLopez.DistributedLock.Abstractions.Tests;

public sealed class DistributedLockAttributeTests
{
    [Fact]
    public void Constructor_ValidResourceKeyPattern_SetsProperty()
    {
        const string pattern = "tenant:{TenantId}:invoices";
        var attr = new DistributedLockAttribute(pattern);

        attr.ResourceKeyPattern.Should().Be(pattern);
        attr.TimeoutSeconds.Should().Be(0); // default timeout is 0 (immediate return)
        attr.Blocking.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidResourceKeyPattern_ThrowsArgumentException(string? invalidPattern)
    {
        var act = () => new DistributedLockAttribute(invalidPattern!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Properties_CanBeCustomized()
    {
        var attr = new DistributedLockAttribute("resource:order:123")
        {
            TimeoutSeconds = 45,
            Blocking = false
        };

        attr.ResourceKeyPattern.Should().Be("resource:order:123");
        attr.TimeoutSeconds.Should().Be(45);
        attr.Blocking.Should().BeFalse();

        attr.Blocking = true;
        attr.Blocking.Should().BeTrue();
    }

    [Fact]
    public void AttributeUsage_HasCorrectTargetsAndConstraints()
    {
        var usage = (AttributeUsageAttribute?)Attribute.GetCustomAttribute(
            typeof(DistributedLockAttribute),
            typeof(AttributeUsageAttribute));

        usage.Should().NotBeNull();
        usage!.ValidOn.Should().Be(AttributeTargets.Class | AttributeTargets.Method);
        usage.AllowMultiple.Should().BeFalse();
        usage.Inherited.Should().BeFalse();
    }
}
