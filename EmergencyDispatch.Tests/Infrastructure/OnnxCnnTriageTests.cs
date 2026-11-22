using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using EmergencyDispatch.Infrastructure.Services;
using EmergencyDispatch.Infrastructure.Services.CnnTriage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmergencyDispatch.Tests.Infrastructure;

/// <summary>
/// 2 CNN của "3 AI bỏ phiếu" chạy trong backend (ONNX Runtime). Giá trị mong đợi sinh bằng Pillow / torchvision /
/// pipeline PyTorch của YoloService (triage_models.py) để bảo đảm tiền xử lý C# khớp lúc train
/// </summary>
public class OnnxCnnTriageTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "TestData", "CnnTriage");
    private const string TrustedUrl = "https://res.cloudinary.com/demo-cloud/image/upload/v1/emergency_dispatch/images/a.jpg";

    private static byte[] Pattern(int w, int h)
    {
        // Cùng công thức với script Python sinh giá trị mong đợi; RGBA, alpha = 255
        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = (y * w + x) * 4;
            rgba[i] = (byte)((x * 37 + y * 91 + x * y % 17) % 256);
            rgba[i + 1] = (byte)((x * 53 + y * 29 + 11) % 256);
            rgba[i + 2] = (byte)((x * x + y * 7 + 101) % 256);
            rgba[i + 3] = 255;
        }
        return rgba;
    }

    [Theory]
    [InlineData(7, 5, 3, 4, "Nz9og6JxpFmCi2Fwh6t5Z2WKYYN4aGyBs2eSeKWAl2KJXIma")]
    [InlineData(5, 3, 11, 7, "AAtlBxVlGC1mKEVmOV1oSnVpW41rbKVufL1xjdV0lN91DQ9mFBlmJTFnNUlnRmFpV3lqaJFsealvisFym9l1ouN2NBxpOyZpTD5qXVZqbm5sf4ZtkJ5vorZyss51w+Z4yvB5WyhsYjJsc0pthGJtlnpvp5JwuKpyysJ129p47PJ78/x8gjRviT5vmlZwom5wgoZyYZ5zc7Z1hM54lr97p6V+rpt/qUFysEtywmNzv3tzbpN1G6t2LcN4Ptt7UKN+YliBaTmCtkVzvU9zz2d0yX90Z5d2BK93Fsd5J998OZp/Sz6CUhmD")]
    [InlineData(10, 10, 10, 4, "TyhsdV1tYZJwh7t1rbZ8ajGFkGaQWZudf8SsZ1a9ZGl8jZ59Y7SAi0WFakeMg3KVpaegb72tl068ZUbNc7KNk6qOdjuRl1GWa4adj7umWLOxfUS+mlrNdY/eh6KdSzSecl2hlJKmgceto6u2bT3Bk2bOTpvdc9Du")]
    [InlineData(9, 6, 4, 6, "HzdmapxurFqAPYeZelRtwbl1XXeHbKSgo3F0OJ58f1yOzsGnMo57g1GDxnOVUn+ujquCom6KOJCcgZy1t5WJTUaRjZqj1ku8")]
    [InlineData(4, 4, 4, 4, "AAtlJUBmSnVpb6puWyhsgV1tp5Jwzcd1tkVz3Xp0BK93K+R8EWJ6OZd7Ycx+iQGD")]
    public void ResizeBilinear_MatchesPillowExactly_SmallImages(int w, int h, int nw, int nh, string expectedBase64)
    {
        var output = PillowResampler.ResizeBilinear(Pattern(w, h), w, h, 4, nw, nh);
        Assert.Equal(expectedBase64, Convert.ToBase64String(output));
    }

    [Theory]
    [InlineData(1000, 750, 512, 512, "b3ae990461f5da66719df6bf279bdd17368b9be9cf936ab21a444794ed9b185d")]
    [InlineData(301, 199, 512, 512, "6266e806ddef9aaa36d844b9c89d92b9626cf6ce068ebbcbc188ca1181ec599c")]
    [InlineData(640, 480, 298, 224, "5a90ab3c8ea6af966e8e0fcf5065619619740e62f10f54b2dd9bd566900258af")]
    public void ResizeBilinear_MatchesPillowExactly_ModelSizes(int w, int h, int nw, int nh, string expectedSha256)
    {
        var output = PillowResampler.ResizeBilinear(Pattern(w, h), w, h, 4, nw, nh);
        Assert.Equal(expectedSha256, Convert.ToHexString(SHA256.HashData(output)).ToLowerInvariant());
    }

    [Theory]
    [InlineData(301, 199, 338, 224, 57, 0)]
    [InlineData(199, 301, 224, 338, 0, 57)]
    [InlineData(640, 480, 298, 224, 37, 0)]
    [InlineData(224, 224, 224, 224, 0, 0)]
    [InlineData(225, 1000, 224, 995, 0, 386)] // (995 - 224) / 2 = 385.5 → Python round → 386
    [InlineData(1000, 333, 672, 224, 224, 0)]
    public void YoloResizeAndCrop_MatchTorchvision(int w, int h, int rw, int rh, int left, int top)
    {
        Assert.Equal((rw, rh), CnnImagePreprocessor.TorchvisionShortSideSize(w, h, 224));
        Assert.Equal((left, top), CnnImagePreprocessor.CenterCropOffset(rw, rh, 224));
    }

    [Fact]
    public void ParseYoloNames_ReadsUltralyticsMetadata()
    {
        var names = OnnxTriageModels.ParseYoloNames("{0: 'accident_L1', 1: 'accident_L2', 2: 'accident_L3', 3: 'fire_L1', 4: 'fire_L2', 5: 'fire_L3', 6: 'none'}");
        Assert.Equal(new[] { "accident_L1", "accident_L2", "accident_L3", "fire_L1", "fire_L2", "fire_L3", "none" }, names);
        Assert.Null(OnnxTriageModels.ParseYoloNames(null));
        Assert.Null(OnnxTriageModels.ParseYoloNames("{1: 'a'}"));
    }

    [Fact]
    public void Softmax_SumsToOne_AndKeepsOrder()
    {
        var p = OnnxTriageModels.Softmax(new[] { 1f, 3f, 2f, 1000f });
        Assert.Equal(1.0, p.Sum(), 5);
        Assert.Equal(3, Array.IndexOf(p, p.Max()));
    }

    public static IEnumerable<object[]> ExpectedImages()
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDir, "expected_pytorch.json"))).RootElement
            .EnumerateObject().Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(ExpectedImages))]
    public async Task RealModels_MatchPyTorchPipeline(string file)
    {
        var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDir, "expected_pytorch.json"))).RootElement.GetProperty(file);
        using var models = new OnnxTriageModels(new ConfigurationBuilder().Build(), NullLogger<OnnxTriageModels>.Instance);

        var votes = await models.ClassifyAsync(CnnImagePreprocessor.Decode(await File.ReadAllBytesAsync(Path.Combine(DataDir, file))));

        Assert.Equal(CnnTriageService.ModelNames, votes.Select(v => v.Model));
        foreach (var (vote, key) in votes.Zip(new[] { "resnet", "yolo" }))
        {
            Assert.True(vote.IsAvailable, vote.Error);
            var label = vote.IncidentType == "none" ? "none" : $"{vote.IncidentType}_L{vote.Severity}";
            Assert.Equal(expected.GetProperty(key)[0].GetString(), label);
            Assert.InRange(Math.Abs(expected.GetProperty(key)[1].GetDouble() - vote.Confidence!.Value), 0, 0.01);
            Assert.Equal("softmax", vote.ConfidenceSource);
        }
    }

    private static IConfiguration TrustedConfig(Dictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Cloudinary:CloudName"] = "demo-cloud",
            ["Cloudinary:ApiKey"] = "k",
            ["Cloudinary:ApiSecret"] = "s"
        };
        foreach (var kv in extra ?? new()) values[kv.Key] = kv.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static OnnxCnnTriageService Service(FakeHttpHandler handler, IConfiguration config)
        => new(new FakeHttpClientFactory().With(OnnxCnnTriageService.DownloadClientName, handler), config,
            new OnnxTriageModels(config, NullLogger<OnnxTriageModels>.Instance), NullLogger<OnnxCnnTriageService>.Instance);

    [Fact]
    public async Task Service_DownloadsPhoto_AndReturnsTwoVotes()
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(DataDir, "accident_L2.jpg"));
        var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });

        var votes = await Service(handler, TrustedConfig()).ClassifyAsync(TrustedUrl);

        Assert.Single(handler.Requests);
        Assert.All(votes, v => Assert.True(v.IsAvailable && v.IncidentType == "accident" && v.Severity == 2 && v.Confidence >= 0.9));
    }

    [Fact]
    public async Task Service_UntrustedUrl_NeverDownloads()
    {
        var handler = new FakeHttpHandler(_ => throw new InvalidOperationException("must not be called"));

        var votes = await Service(handler, TrustedConfig()).ClassifyAsync("http://169.254.169.254/latest/meta-data");

        Assert.Empty(handler.Requests);
        Assert.Equal(2, votes.Count);
        Assert.All(votes, v => Assert.False(v.IsAvailable));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "image/jpeg", "download failed")]
    [InlineData(HttpStatusCode.OK, "text/html", "decode failed")]
    public async Task Service_BadDownloadOrNonImage_ReturnsUnavailableVotes(HttpStatusCode status, string contentType, string expectedError)
    {
        var handler = new FakeHttpHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("<html>not an image</html>", System.Text.Encoding.UTF8, contentType)
        });

        var votes = await Service(handler, TrustedConfig()).ClassifyAsync(TrustedUrl);

        Assert.Equal(2, votes.Count);
        Assert.All(votes, v =>
        {
            Assert.False(v.IsAvailable);
            Assert.Contains(expectedError, v.Error, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Service_MissingModelFiles_ReturnsUnavailableVotes()
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(DataDir, "none.jpg"));
        var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var config = TrustedConfig(new()
        {
            ["CnnTriage:ResNetModelPath"] = "AiModels/missing_resnet.onnx",
            ["CnnTriage:YoloModelPath"] = "AiModels/missing_yolo.onnx"
        });

        var votes = await Service(handler, config).ClassifyAsync(TrustedUrl);

        Assert.All(votes, v =>
        {
            Assert.False(v.IsAvailable);
            Assert.Contains("not found", v.Error);
        });
    }

    [Fact]
    public async Task Service_ImageOverSizeLimit_IsRejected()
    {
        var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[OnnxCnnTriageService.MaxImageBytes + 1])
        });

        var votes = await Service(handler, TrustedConfig()).ClassifyAsync(TrustedUrl);

        Assert.All(votes, v => Assert.Contains("larger than", v.Error));
    }
}
