using System.Linq;
using MFGUnlockerGUI.Core;
using Xunit;

namespace MFGUnlockerGUI.Tests
{
    public class ProxyNamesTests
    {
        [Fact]
        public void DefaultOrder_StartsVersionThenWinmm()
        {
            var o = ProxyNames.DefaultOrder;
            Assert.Equal("version.dll", o[0]);
            Assert.Equal("winmm.dll", o[1]);
            Assert.Equal(17, o.Count);
            Assert.DoesNotContain("binkw64.dll", o);
            Assert.DoesNotContain("bink2w64.dll", o);
        }

        [Fact]
        public void IsSupported_IgnoresCase()
        {
            Assert.True(ProxyNames.IsSupported("VERSION.DLL"));
            Assert.False(ProxyNames.IsSupported("foo.dll"));
            Assert.True(ProxyNames.IsSupported("binkw64.dll"));
        }

        [Fact]
        public void OrderFor_REEngine_ExcludesDinput8()
        {
            Assert.DoesNotContain("dinput8.dll", ProxyNames.OrderFor(Engine.REEngine));
        }

        [Fact]
        public void OrderFor_REDengine_IsDefaultOrder()
        {
            Assert.Equal(ProxyNames.DefaultOrder.ToList(), ProxyNames.OrderFor(Engine.REDengine).ToList());
        }

        [Theory]
        [InlineData(Engine.Unreal)]
        [InlineData(Engine.Unity)]
        [InlineData(Engine.Unknown)]
        public void OrderFor_UnrealUnityUnknown_EqualsDefault(Engine e)
        {
            Assert.Equal(ProxyNames.DefaultOrder.ToList(), ProxyNames.OrderFor(e).ToList());
        }
    }
}
