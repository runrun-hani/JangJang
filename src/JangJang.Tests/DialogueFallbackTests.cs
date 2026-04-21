using JangJang.Core;
using JangJang.Core.Persona;
using Xunit;

namespace JangJang.Tests;

/// <summary>
/// Dialogue.cs의 Provider 폴백 체인을 검증한다.
/// 페르소나 Provider가 빈 문자열(파이프라인 실패/후보 0)을 반환할 때
/// 자동으로 기본 Provider로 폴백하는 동작을 확인.
///
/// Dialogue._current는 정적 상태라 테스트 간 간섭 가능성이 있음.
/// [Collection]으로 이 클래스의 테스트들을 직렬화하고, 각 테스트 후 ResetToDefault로 정리.
/// </summary>
[Collection("DialogueStaticState")]
public class DialogueFallbackTests : IDisposable
{
    public DialogueFallbackTests()
    {
        Dialogue.ResetToDefault();
    }

    public void Dispose()
    {
        Dialogue.ResetToDefault();
    }

    private class EmptyProvider : IDialogueProvider
    {
        public string GetLine(DialogueContext context) => string.Empty;
    }

    private class FixedProvider : IDialogueProvider
    {
        private readonly string _value;
        public FixedProvider(string value) { _value = value; }
        public string GetLine(DialogueContext context) => _value;
    }

    [Fact]
    public void GetLine_WithEmptyProvider_FallsBackToDefaultHappyLine()
    {
        Dialogue.SetProvider(new EmptyProvider());
        var line = Dialogue.GetLine(PetState.Happy, 0.0, 0);

        Assert.NotEmpty(line);
        var happyLines = new[] { "열심히 일하는 중!", "집중!", "좋아", "굿", "이 느낌", "오" };
        Assert.Contains(line, happyLines);
    }

    [Fact]
    public void GetLine_WithFixedProvider_ReturnsFixedValue()
    {
        Dialogue.SetProvider(new FixedProvider("자캐 전용 대사"));
        var line = Dialogue.GetLine(PetState.Happy, 0.0, 0);

        Assert.Equal("자캐 전용 대사", line);
    }
}

/// <summary>
/// xUnit Collection Fixture. Dialogue의 정적 상태를 공유하는 테스트들을 직렬화.
/// </summary>
[CollectionDefinition("DialogueStaticState")]
public class DialogueStaticStateCollection { }
