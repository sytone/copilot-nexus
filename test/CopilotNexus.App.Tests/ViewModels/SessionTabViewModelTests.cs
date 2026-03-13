namespace CopilotNexus.App.Tests.ViewModels;

using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using CopilotNexus.App.Tests.Helpers;
using CopilotNexus.App.ViewModels;
using CopilotNexus.Core.Events;
using CopilotNexus.Core.Interfaces;
using CopilotNexus.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class SessionTabViewModelTests : IDisposable
{
    private readonly Mock<ICopilotSessionWrapper> _mockSession;
    private readonly SessionInfo _sessionInfo;
    private readonly SynchronousUiDispatcher _dispatcher;
    private readonly SessionTabViewModel _viewModel;

    public SessionTabViewModelTests()
    {
        _mockSession = new Mock<ICopilotSessionWrapper>();
        _sessionInfo = new SessionInfo("Test Session", "gpt-4.1");
        _dispatcher = new SynchronousUiDispatcher();
        _viewModel = new SessionTabViewModel(_sessionInfo, _mockSession.Object, _dispatcher, NullLogger.Instance);
    }

    [Fact]
    public void Constructor_SetsTitle()
    {
        Assert.Equal("Test Session", _viewModel.Title);
        Assert.Equal("Test Session", _viewModel.EditableTitle);
        Assert.Equal("Autopilot", _viewModel.SelectedMode);
    }

    [Fact]
    public void Constructor_SetsInfo()
    {
        Assert.Same(_sessionInfo, _viewModel.Info);
    }

    [Fact]
    public void Constructor_IsRunningTrue()
    {
        Assert.True(_viewModel.IsRunning);
    }

    [Fact]
    public void Constructor_HasSystemStartMessage()
    {
        Assert.Single(_viewModel.Messages);
        Assert.Equal(MessageRole.System, _viewModel.Messages[0].Role);
        Assert.Contains("gpt-4.1", _viewModel.Messages[0].Content);
    }

    [Fact]
    public void Constructor_IsProcessingFalse()
    {
        Assert.False(_viewModel.IsProcessing);
    }

    [Fact]
    public void SendCommand_WhenProcessing_CannotExecute()
    {
        _viewModel.InputText = "hello";
        _viewModel.IsProcessing = true;

        Assert.False(_viewModel.SendCommand.CanExecute(null));
    }

    [Fact]
    public void SendCommand_WhenRunningWithInput_CanExecute()
    {
        _viewModel.InputText = "hello";

        Assert.True(_viewModel.SendCommand.CanExecute(null));
    }

    [Fact]
    public void SendCommand_WhenEmptyInput_CannotExecute()
    {
        _viewModel.InputText = "";

        Assert.False(_viewModel.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task SendCommand_Execute_SendsInputAndClearsTextBox()
    {
        _viewModel.InputText = "hello world";

        _viewModel.SendCommand.Execute(null);
        await Task.Delay(100);

        _mockSession.Verify(
            s => s.SendAsync("hello world", It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(string.Empty, _viewModel.InputText);
    }

    [Fact]
    public async Task SendCommand_Execute_AddsUserMessage()
    {
        _viewModel.InputText = "my message";

        _viewModel.SendCommand.Execute(null);
        await Task.Delay(100);

        Assert.Contains(_viewModel.Messages, m =>
            m.Role == MessageRole.User && m.Content == "my message");
    }

    [Fact]
    public async Task TryNavigateInputHistory_UpAndDownNavigatePerSessionInputs()
    {
        _viewModel.InputText = "first";
        _viewModel.SendCommand.Execute(null);
        await Task.Delay(100);

        _viewModel.InputText = "second";
        _viewModel.SendCommand.Execute(null);
        await Task.Delay(100);

        _viewModel.InputText = "draft";

        Assert.True(_viewModel.TryNavigateInputHistory(-1));
        Assert.Equal("second", _viewModel.InputText);

        Assert.True(_viewModel.TryNavigateInputHistory(-1));
        Assert.Equal("first", _viewModel.InputText);

        Assert.True(_viewModel.TryNavigateInputHistory(1));
        Assert.Equal("second", _viewModel.InputText);

        Assert.True(_viewModel.TryNavigateInputHistory(1));
        Assert.Equal("draft", _viewModel.InputText);
    }

    [Fact]
    public void TryNavigateInputHistory_WithNoHistory_ReturnsFalse()
    {
        var moved = _viewModel.TryNavigateInputHistory(-1);

        Assert.False(moved);
    }

    [Fact]
    public void AbortCommand_WhenNotProcessing_CannotExecute()
    {
        Assert.False(_viewModel.AbortCommand.CanExecute(null));
    }

    [Fact]
    public void ClearCommand_ClearsAllMessages()
    {
        _viewModel.Messages.Add(new SessionMessage(MessageRole.User, "test"));
        _viewModel.Messages.Add(new SessionMessage(MessageRole.Assistant, "response"));

        _viewModel.ClearCommand.Execute(null);

        Assert.Empty(_viewModel.Messages);
    }

    [Fact]
    public void RequestClose_RaisesCloseRequestedEvent()
    {
        var raised = false;
        _viewModel.CloseRequested += (_, _) => raised = true;

        _viewModel.RequestClose();

        Assert.True(raised);
    }

    [Fact]
    public void RenameSessionCommand_RaisesRenameRequested()
    {
        string? renamed = null;
        _viewModel.RenameRequested += (_, value) => renamed = value;

        _viewModel.EditableTitle = "Friendly Name";
        _viewModel.RenameSessionCommand.Execute(null);

        Assert.Equal("Friendly Name", renamed);
    }

    [Fact]
    public void BeginInlineRename_EnablesInlineEditState()
    {
        _viewModel.BeginInlineRename();

        Assert.True(_viewModel.IsInlineRenaming);
        Assert.Equal("Test Session", _viewModel.EditableTitle);
    }

    [Fact]
    public void CommitInlineRename_RaisesRenameRequestedAndExitsInlineEdit()
    {
        string? renamed = null;
        _viewModel.RenameRequested += (_, value) => renamed = value;

        _viewModel.BeginInlineRename();
        _viewModel.EditableTitle = "Header Rename";
        _viewModel.CommitInlineRename();

        Assert.False(_viewModel.IsInlineRenaming);
        Assert.Equal("Header Rename", renamed);
    }

    [Fact]
    public void ApplyRename_UpdatesTitleAndEditableTitle()
    {
        _viewModel.BeginInlineRename();
        _viewModel.ApplyRename("New Name");

        Assert.Equal("New Name", _viewModel.Title);
        Assert.Equal("New Name", _viewModel.EditableTitle);
        Assert.Equal("New Name", _viewModel.Info.Name);
        Assert.False(_viewModel.IsInlineRenaming);
    }

    [Fact]
    public void SelectedMode_SetToPlan_KeepsInteractiveMode()
    {
        _viewModel.SelectedMode = "Plan";

        Assert.Equal("Plan", _viewModel.SelectedMode);
        Assert.False(_viewModel.Info.IsAutopilot);
    }

    [Fact]
    public void SelectedMode_SetToAutopilot_RaisesReconfigureRequest()
    {
        var info = new SessionInfo("Mode Test", "gpt-4.1")
        {
            IsAutopilot = false,
        };
        var vm = new SessionTabViewModel(info, _mockSession.Object, _dispatcher, NullLogger.Instance);
        SessionConfiguration? requestedConfig = null;
        vm.ReconfigureRequested += (_, cfg) => requestedConfig = cfg;

        vm.SelectedMode = "Autopilot";

        Assert.NotNull(requestedConfig);
        Assert.True(requestedConfig!.IsAutopilot);
        Assert.True(vm.Info.IsAutopilot);
    }

    [Fact]
    public void SelectedModel_WithReasoningAndCost_UpdatesMetadataDisplay()
    {
        var model = new ModelInfo
        {
            ModelId = "gpt-5",
            Name = "GPT-5",
            Capabilities = new List<string> { "reasoning:high", "cost:$0.25/1K tok" },
        };
        var models = new ObservableCollection<ModelInfo> { model };
        var vm = new SessionTabViewModel(_sessionInfo, _mockSession.Object, _dispatcher, NullLogger.Instance, models);

        vm.SelectedModel = model;

        Assert.Equal("GPT-5", vm.CurrentModelDisplay);
        Assert.Equal("high", vm.ReasoningLevelDisplay);
        Assert.Equal("$0.25/1K tok", vm.ModelCostDisplay);
    }

    [Fact]
    public void Constructor_SelectsProfileMatchingSessionInfo()
    {
        var info = new SessionInfo("Profile Session", "gpt-4.1")
        {
            ProfileId = "dev-profile",
        };
        var profiles = new ObservableCollection<SessionProfile>
        {
            new() { Id = "default", Name = "Default" },
            new() { Id = "dev-profile", Name = "Dev Agent" },
        };

        var vm = new SessionTabViewModel(
            info,
            _mockSession.Object,
            _dispatcher,
            NullLogger.Instance,
            availableProfiles: profiles);

        Assert.NotNull(vm.SelectedProfile);
        Assert.Equal("dev-profile", vm.SelectedProfile!.Id);
    }

    [Fact]
    public void SelectedProfile_RaisesReconfigureWithProfileSettings()
    {
        var info = new SessionInfo("Profile Reconfigure", "gpt-4.1")
        {
            IsAutopilot = true,
        };
        var profile = new SessionProfile
        {
            Id = "analysis",
            Name = "Analysis Agent",
            Model = "anthropic/claude-sonnet-4-5-20250929",
            IsAutopilot = false,
            WorkingDirectory = @"Q:\repos\gh\copilot-nexus",
            AgentFilePath = @"Q:\agents\analysis.md",
            IncludeWellKnownMcpConfigs = false,
            AdditionalMcpConfigPaths = @"Q:\mcp\a.json;Q:\mcp\b.json",
            EnabledMcpServers = "context7,github",
            AdditionalSkillDirectories = @"Q:\skills\custom",
        };
        var profiles = new ObservableCollection<SessionProfile> { profile };

        var vm = new SessionTabViewModel(
            info,
            _mockSession.Object,
            _dispatcher,
            NullLogger.Instance,
            availableProfiles: profiles);
        SessionConfiguration? requested = null;
        vm.ReconfigureRequested += (_, config) => requested = config;

        vm.SelectedProfile = profile;

        Assert.NotNull(requested);
        Assert.Equal("analysis", requested!.ProfileId);
        Assert.Equal("anthropic/claude-sonnet-4-5-20250929", requested.Model);
        Assert.False(requested.IsAutopilot);
        Assert.Equal(@"Q:\repos\gh\copilot-nexus", requested.WorkingDirectory);
        Assert.Equal(@"Q:\agents\analysis.md", requested.AgentFilePath);
        Assert.False(requested.IncludeWellKnownMcpConfigs);
        Assert.Equal(2, requested.AdditionalMcpConfigPaths.Count);
        Assert.Equal(2, requested.EnabledMcpServers.Count);
        Assert.Single(requested.SkillDirectories);
    }

    [Fact]
    public void WorkingDirectory_WhenGitRepository_ShowsBranch()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"nexus-git-test-{Guid.NewGuid():N}");
        var gitDir = Path.Combine(tempRoot, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/feature/test-footer");

        try
        {
            _viewModel.WorkingDirectory = tempRoot;

            Assert.Equal(tempRoot, _viewModel.WorkingDirectoryDisplay);
            Assert.Equal("test-footer", _viewModel.GitBranchDisplay);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void DeltaOutput_CreatesStreamingMessage()
    {
        _mockSession.Raise(
            s => s.OutputReceived += null,
            _mockSession.Object,
            new SessionOutputEventArgs("test", "Hello ", MessageRole.Assistant, OutputKind.Delta));

        // System start message + streaming message
        Assert.Equal(2, _viewModel.Messages.Count);
        var streamMsg = _viewModel.Messages[1];
        Assert.Equal("Hello ", streamMsg.Content);
        Assert.True(streamMsg.IsStreaming);
    }

    [Fact]
    public void DeltaOutput_AppendsToExistingStreamingMessage()
    {
        _mockSession.Raise(s => s.OutputReceived += null, _mockSession.Object,
            new SessionOutputEventArgs("test", "Hello ", MessageRole.Assistant, OutputKind.Delta));
        _mockSession.Raise(s => s.OutputReceived += null, _mockSession.Object,
            new SessionOutputEventArgs("test", "World", MessageRole.Assistant, OutputKind.Delta));

        Assert.Equal(2, _viewModel.Messages.Count); // system + 1 streaming msg
        Assert.Equal("Hello World", _viewModel.Messages[1].Content);
    }

    [Fact]
    public void ReasoningDeltaOutput_CreatesStreamingThinkingMessage()
    {
        _mockSession.Raise(s => s.OutputReceived += null, _mockSession.Object,
            new SessionOutputEventArgs("test", "Considering options...", MessageRole.System, OutputKind.ReasoningDelta));

        Assert.Equal(2, _viewModel.Messages.Count);
        var thinking = _viewModel.Messages[1];
        Assert.Equal(MessageRole.System, thinking.Role);
        Assert.Contains("[thinking]", thinking.Content);
        Assert.Contains("Considering options...", thinking.Content);
        Assert.True(thinking.IsStreaming);
    }

    [Fact]
    public void ActivityOutput_AppendsActivityMessage()
    {
        _mockSession.Raise(s => s.OutputReceived += null, _mockSession.Object,
            new SessionOutputEventArgs("test", "Tool started: rg", MessageRole.System, OutputKind.Activity));

        Assert.Equal(2, _viewModel.Messages.Count);
        Assert.Equal("[activity] Tool started: rg", _viewModel.Messages[1].Content);
    }

    [Fact]
    public void ActivityOutput_WithSameCorrelation_UpdatesPreviousMessage()
    {
        _mockSession.Raise(s => s.OutputReceived += null, _mockSession.Object,
            new SessionOutputEventArgs("test", "Tool started: rg", MessageRole.System, OutputKind.Activity, "tool-1"));
        _mockSession.Raise(s => s.OutputReceived += null, _mockSession.Object,
            new SessionOutputEventArgs("test", "Tool progress: 50%", MessageRole.System, OutputKind.Activity, "tool-1"));

        Assert.Equal(2, _viewModel.Messages.Count);
        Assert.Equal("[activity] Tool progress: 50%", _viewModel.Messages[1].Content);
    }

    [Fact]
    public void IdleOutput_CompletesStreamingMessage()
    {
        _mockSession.Raise(s => s.OutputReceived += null, _mockSession.Object,
            new SessionOutputEventArgs("test", "response", MessageRole.Assistant, OutputKind.Delta));
        _mockSession.Raise(s => s.OutputReceived += null, _mockSession.Object,
            new SessionOutputEventArgs("test", "", MessageRole.System, OutputKind.Idle));

        Assert.False(_viewModel.Messages[1].IsStreaming);
    }

    [Fact]
    public void PropertyChanged_RaisedForInputText()
    {
        var changedProps = new List<string>();
        _viewModel.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        _viewModel.InputText = "new text";

        Assert.Contains("InputText", changedProps);
    }

    [Fact]
    public void PropertyChanged_RaisedForIsProcessing()
    {
        var changedProps = new List<string>();
        _viewModel.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        _viewModel.IsProcessing = true;

        Assert.Contains("IsProcessing", changedProps);
    }

    [Fact]
    public void Dispose_UnsubscribesFromEvents()
    {
        _viewModel.Dispose();

        _mockSession.Raise(s => s.OutputReceived += null, _mockSession.Object,
            new SessionOutputEventArgs("test", "after", MessageRole.Assistant, OutputKind.Delta));

        // Only the system start message from constructor
        Assert.Single(_viewModel.Messages);
    }

    public void Dispose()
    {
        _viewModel.Dispose();
    }
}
