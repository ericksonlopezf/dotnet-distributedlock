// Copyright © Erickson Lopez. MIT License.
using EricksonLopez.DistributedLock.Abstractions;
using FluentAssertions;
using Xunit;

namespace EricksonLopez.DistributedLock.Abstractions.Tests;

public sealed class DistributedLockErrorsTests
{
    [Fact]
    public void DistributedLockErrors_HaveCorrectCodes()
    {
        DistributedLockErrors.LockAlreadyHeld.Code.Should().Be("DistributedLock.AlreadyHeld");
        DistributedLockErrors.Timeout.Code.Should().Be("DistributedLock.Timeout");
        DistributedLockErrors.LockLost.Code.Should().Be("DistributedLock.Lost");
        DistributedLockErrors.Canceled.Code.Should().Be("DistributedLock.Canceled");
    }

    [Fact]
    public void DistributedLockErrors_HaveDescriptiveMessages()
    {
        DistributedLockErrors.LockAlreadyHeld.Description.Should().NotBeNullOrWhiteSpace();
        DistributedLockErrors.Timeout.Description.Should().NotBeNullOrWhiteSpace();
        DistributedLockErrors.LockLost.Description.Should().NotBeNullOrWhiteSpace();
        DistributedLockErrors.Canceled.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void DistributedLockErrors_AllCodesAreUnique()
    {
        var codes = new[]
        {
            DistributedLockErrors.LockAlreadyHeld.Code,
            DistributedLockErrors.Timeout.Code,
            DistributedLockErrors.LockLost.Code,
            DistributedLockErrors.Canceled.Code
        };

        codes.Should().OnlyHaveUniqueItems();
    }
}
