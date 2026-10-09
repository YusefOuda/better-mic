namespace BetterMic.Tests;

public class DevicePolicyTests
{
    static readonly AudioDevice Headset = new("headset", "Microphone (Arctis Nova 3X)", false);
    static readonly AudioDevice Webcam = new("webcam", "Microphone (Webcam)", false);
    static readonly AudioDevice Cable = new("cable", "CABLE Output (VB-Audio Virtual Cable)", true);
    static readonly List<AudioDevice> All = [Cable, Headset, Webcam];

    [Theory]
    [InlineData("VB-Audio Virtual Cable", true)]
    [InlineData("vb-audio virtual cable", true)]
    [InlineData("Arctis Nova 3X", false)]
    public void DetectsVirtualCableByInterfaceName(string name, bool expected) =>
        Assert.Equal(expected, DevicePolicy.IsVirtualCable(name));

    [Fact]
    public void PrefersChosenMic() =>
        Assert.Equal("webcam", DevicePolicy.PickMic("webcam", All, "headset", "headset"));

    [Fact]
    public void FallsBackToDefaultWhenChosenMicIsGone() =>
        Assert.Equal("headset", DevicePolicy.PickMic("unplugged", All, "headset", null));

    // The recommended setup makes CABLE Output the Windows default mic; capturing it would loop our output back
    [Fact]
    public void NeverPicksTheVirtualCable()
    {
        Assert.Equal("headset", DevicePolicy.PickMic(null, All, "cable", "headset"));
        Assert.Equal("headset", DevicePolicy.PickMic("cable", All, "cable", "cable"));
        Assert.Null(DevicePolicy.PickMic(null, [Cable], "cable", "cable"));
    }

    [Fact]
    public void AutomaticSkipsOtherVirtualMicsButHonorsExplicitChoice()
    {
        var broadcast = new AudioDevice("nvb", "Microphone (NVIDIA Broadcast)", false, IsOtherVirtual: true);
        List<AudioDevice> devices = [Cable, broadcast, Headset];
        Assert.Equal("headset", DevicePolicy.PickMic(null, devices, "nvb", "nvb"));
        Assert.Equal("nvb", DevicePolicy.PickMic("nvb", devices, "headset", "headset"));
        Assert.Null(DevicePolicy.PickMic(null, [Cable, broadcast], "nvb", "nvb"));
    }

    [Theory]
    [InlineData("NVIDIA Broadcast", true)]
    [InlineData("Steam Streaming Microphone", true)]
    [InlineData("VB-Audio VoiceMeeter VAIO", true)]
    [InlineData("VB-Audio Virtual Cable", false)] // that's ours, not "other"
    [InlineData("Arctis Nova 3X Wireless", false)]
    public void DetectsOtherVirtualDevices(string iface, bool expected) =>
        Assert.Equal(expected, DevicePolicy.IsOtherVirtual(iface));

    [Fact]
    public void PrefersRegularCableInputOver16Channel()
    {
        List<AudioDevice> renders =
        [
            new("c16", "CABLE In 16 Ch (VB-Audio Virtual Cable)", true),
            new("cin", "Speakers (VB-Audio Virtual Cable)", true),
        ];
        Assert.Equal("cin", DevicePolicy.PickCableInput(renders));
    }

    [Fact]
    public void PicksCableInputFromRenderDevices()
    {
        List<AudioDevice> renders = [new("spk", "Speakers", false), new("cin", "CABLE Input (VB-Audio Virtual Cable)", true)];
        Assert.Equal("cin", DevicePolicy.PickCableInput(renders));
        Assert.Null(DevicePolicy.PickCableInput([new("spk", "Speakers", false)]));
    }

    [Theory]
    [InlineData(AecMode.Auto, FormFactor.Speakers, true)]
    [InlineData(AecMode.Auto, FormFactor.DigitalAudioDisplayDevice, true)]
    [InlineData(AecMode.Auto, FormFactor.Headphones, false)]
    [InlineData(AecMode.Auto, FormFactor.Headset, false)]
    [InlineData(AecMode.Auto, null, true)]
    [InlineData(AecMode.On, FormFactor.Headphones, true)]
    [InlineData(AecMode.Off, FormFactor.Speakers, false)]
    public void AecFollowsModeAndOutputType(AecMode mode, FormFactor? output, bool expected) =>
        Assert.Equal(expected, DevicePolicy.IsAecEnabled(mode, output));
}
