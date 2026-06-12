using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentGroupChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiConnections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Transport = table.Column<string>(type: "TEXT", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiModels",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<string>(type: "TEXT", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    Temperature = table.Column<decimal>(type: "TEXT", nullable: false),
                    MaxTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiModels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UiTheme = table.Column<string>(type: "TEXT", nullable: false),
                    UiAccent = table.Column<string>(type: "TEXT", nullable: false),
                    TtsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    TtsProvider = table.Column<string>(type: "TEXT", nullable: false),
                    TtsVoice = table.Column<string>(type: "TEXT", nullable: false),
                    TtsRate = table.Column<int>(type: "INTEGER", nullable: false),
                    PiperExePath = table.Column<string>(type: "TEXT", nullable: false),
                    PiperModelsDir = table.Column<string>(type: "TEXT", nullable: false),
                    KokoroBaseUrl = table.Column<string>(type: "TEXT", nullable: false),
                    KokoroModel = table.Column<string>(type: "TEXT", nullable: false),
                    KokoroVoice = table.Column<string>(type: "TEXT", nullable: false),
                    KokoroUserVoice = table.Column<string>(type: "TEXT", nullable: false),
                    KokoroLangCode = table.Column<string>(type: "TEXT", nullable: false),
                    KokoroSpeed = table.Column<double>(type: "REAL", nullable: false),
                    SetupModelsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    SetupRoomsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    SetupTtsStatus = table.Column<string>(type: "TEXT", nullable: false),
                    HideSetupGuide = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImageConnections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Transport = table.Column<string>(type: "TEXT", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImageModels",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<string>(type: "TEXT", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<string>(type: "TEXT", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    Steps = table.Column<int>(type: "INTEGER", nullable: true),
                    GuidanceScale = table.Column<double>(type: "REAL", nullable: true),
                    NegativePrompt = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageModels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LogEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    Detail = table.Column<string>(type: "TEXT", nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MemoryBlocks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomId = table.Column<string>(type: "TEXT", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryBlocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromptSamples",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    PromptText = table.Column<string>(type: "TEXT", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    ParentPromptSampleId = table.Column<string>(type: "TEXT", nullable: false),
                    SourceLabel = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptSamples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rooms",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Topic = table.Column<string>(type: "TEXT", nullable: false),
                    WaitForUserReply = table.Column<bool>(type: "INTEGER", nullable: false),
                    PauseAfterEveryReply = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgentDelaySeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    RecentTurnsWindow = table.Column<int>(type: "INTEGER", nullable: false),
                    UserCompactionBudget = table.Column<int>(type: "INTEGER", nullable: false),
                    UseSummarizer = table.Column<bool>(type: "INTEGER", nullable: false),
                    StoreSharedRoomMemory = table.Column<bool>(type: "INTEGER", nullable: false),
                    StoreDurableMemory = table.Column<bool>(type: "INTEGER", nullable: false),
                    StoreLongTermArchives = table.Column<bool>(type: "INTEGER", nullable: false),
                    SummarizerModelId = table.Column<string>(type: "TEXT", nullable: false),
                    SummarizationLevel = table.Column<string>(type: "TEXT", nullable: false),
                    SummarizerMaxTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    SummarizerMaxLines = table.Column<int>(type: "INTEGER", nullable: false),
                    SummarizerMaxCharacters = table.Column<int>(type: "INTEGER", nullable: false),
                    SummarizerBroaderTurns = table.Column<int>(type: "INTEGER", nullable: false),
                    SummarizerPromptOverride = table.Column<string>(type: "TEXT", nullable: false),
                    SharedRoomMemoryPromptSampleId = table.Column<int>(type: "INTEGER", nullable: false),
                    DurableMemoryPromptSampleId = table.Column<int>(type: "INTEGER", nullable: false),
                    TtsEnabledOverride = table.Column<bool>(type: "INTEGER", nullable: true),
                    TtsProviderOverride = table.Column<string>(type: "TEXT", nullable: false),
                    TtsFallbackVoice = table.Column<string>(type: "TEXT", nullable: false),
                    TtsUserVoice = table.Column<string>(type: "TEXT", nullable: false),
                    EnableSceneImageGeneration = table.Column<bool>(type: "INTEGER", nullable: false),
                    UseCreativeImageGeneration = table.Column<bool>(type: "INTEGER", nullable: false),
                    SceneImageModelId = table.Column<string>(type: "TEXT", nullable: false),
                    SceneImageStyleNotes = table.Column<string>(type: "TEXT", nullable: false),
                    SceneImageNegativePrompt = table.Column<string>(type: "TEXT", nullable: false),
                    MemoryModelId = table.Column<string>(type: "TEXT", nullable: false),
                    MaxArchivedScenes = table.Column<int>(type: "INTEGER", nullable: false),
                    EnableSceneArchive = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnablePrivilegedActions = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableNpcSpawning = table.Column<bool>(type: "INTEGER", nullable: false),
                    PrivilegedAgentId = table.Column<string>(type: "TEXT", nullable: false),
                    NpcModelId = table.Column<string>(type: "TEXT", nullable: false),
                    NpcPromptSampleId = table.Column<int>(type: "INTEGER", nullable: false),
                    NpcDefaultMaleVoice = table.Column<string>(type: "TEXT", nullable: false),
                    NpcDefaultFemaleVoice = table.Column<string>(type: "TEXT", nullable: false),
                    NpcMaxTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    NpcCompactionBudget = table.Column<int>(type: "INTEGER", nullable: false),
                    NpcBaseInstructions = table.Column<string>(type: "TEXT", nullable: false),
                    MaxConcurrentNpcs = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rooms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SceneArchives",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomId = table.Column<string>(type: "TEXT", nullable: false),
                    RoundNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    KeyEntities = table.Column<string>(type: "TEXT", nullable: false),
                    SharedRoomSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    DurableSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    IsMajor = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SceneArchives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TranscriptTurns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomId = table.Column<string>(type: "TEXT", nullable: false),
                    Round = table.Column<int>(type: "INTEGER", nullable: false),
                    Speaker = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    AccentHex = table.Column<string>(type: "TEXT", nullable: false),
                    BackgroundHex = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranscriptTurns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Agents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RoomId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", nullable: false),
                    SystemPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxTokensOverride = table.Column<int>(type: "INTEGER", nullable: true),
                    CompactionBudget = table.Column<int>(type: "INTEGER", nullable: false),
                    AccentHex = table.Column<string>(type: "TEXT", nullable: false),
                    BackgroundHex = table.Column<string>(type: "TEXT", nullable: false),
                    TtsVoice = table.Column<string>(type: "TEXT", nullable: false),
                    AppearanceSummary = table.Column<string>(type: "TEXT", nullable: false),
                    IsNpc = table.Column<bool>(type: "INTEGER", nullable: false),
                    SpawnedByAgentId = table.Column<string>(type: "TEXT", nullable: false),
                    IsTemporarilySuspended = table.Column<bool>(type: "INTEGER", nullable: false),
                    SuspendedByAgentId = table.Column<string>(type: "TEXT", nullable: false),
                    SuspendedUntilRound = table.Column<int>(type: "INTEGER", nullable: true),
                    SuspensionReason = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsHumanParticipant = table.Column<bool>(type: "INTEGER", nullable: false),
                    PromptSampleId = table.Column<int>(type: "INTEGER", nullable: false),
                    UseShortTermMemoryStorage = table.Column<bool>(type: "INTEGER", nullable: false),
                    UseLongTermMemoryStorage = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Agents_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DataTracking",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomId = table.Column<string>(type: "TEXT", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", nullable: true),
                    DataKey = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    ValueType = table.Column<string>(type: "TEXT", nullable: false),
                    MinValue = table.Column<double>(type: "REAL", nullable: true),
                    MaxValue = table.Column<double>(type: "REAL", nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PromptText = table.Column<string>(type: "TEXT", nullable: false),
                    PrivilegedAgentPrompt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataTracking", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DataTracking_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HumanParticipants",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RoomId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsPlayerCharacter = table.Column<bool>(type: "INTEGER", nullable: false),
                    AppearanceSummary = table.Column<string>(type: "TEXT", nullable: false),
                    TtsVoice = table.Column<string>(type: "TEXT", nullable: false),
                    AccentHex = table.Column<string>(type: "TEXT", nullable: false),
                    BackgroundHex = table.Column<string>(type: "TEXT", nullable: false),
                    ParticipationMode = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HumanParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HumanParticipants_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Agents_RoomId",
                table: "Agents",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DataTracking_RoomId_AgentId_DataKey",
                table: "DataTracking",
                columns: new[] { "RoomId", "AgentId", "DataKey" },
                unique: true,
                filter: "\"AgentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DataTracking_RoomId_DataKey",
                table: "DataTracking",
                columns: new[] { "RoomId", "DataKey" },
                unique: true,
                filter: "\"AgentId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HumanParticipants_RoomId",
                table: "HumanParticipants",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageConnections_Name",
                table: "ImageConnections",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_ImageModels_ConnectionId",
                table: "ImageModels",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageModels_Name",
                table: "ImageModels",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_LogEntries_Category",
                table: "LogEntries",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_LogEntries_Source",
                table: "LogEntries",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_MemoryBlocks_RoomId_AgentId_Kind",
                table: "MemoryBlocks",
                columns: new[] { "RoomId", "AgentId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromptSamples_Category",
                table: "PromptSamples",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_PromptSamples_IsBuiltIn",
                table: "PromptSamples",
                column: "IsBuiltIn");

            migrationBuilder.CreateIndex(
                name: "IX_PromptSamples_Name",
                table: "PromptSamples",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_PromptSamples_UpdatedAt",
                table: "PromptSamples",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SceneArchives_RoomId",
                table: "SceneArchives",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_SceneArchives_RoomId_RoundNumber",
                table: "SceneArchives",
                columns: new[] { "RoomId", "RoundNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_TranscriptTurns_RoomId",
                table: "TranscriptTurns",
                column: "RoomId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Agents");

            migrationBuilder.DropTable(
                name: "AiConnections");

            migrationBuilder.DropTable(
                name: "AiModels");

            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "DataTracking");

            migrationBuilder.DropTable(
                name: "HumanParticipants");

            migrationBuilder.DropTable(
                name: "ImageConnections");

            migrationBuilder.DropTable(
                name: "ImageModels");

            migrationBuilder.DropTable(
                name: "LogEntries");

            migrationBuilder.DropTable(
                name: "MemoryBlocks");

            migrationBuilder.DropTable(
                name: "PromptSamples");

            migrationBuilder.DropTable(
                name: "SceneArchives");

            migrationBuilder.DropTable(
                name: "TranscriptTurns");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Rooms");
        }
    }
}
