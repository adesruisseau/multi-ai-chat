# OLD WPF App:
root/AgentGroupChat.csproj

# New Blazor/Maui App:
root/hybrid/src/AgentGroupChat.Hybrid/AgentGroupChat.Hybrid.csproj


### For narrative functionality, I highly recommend Kokoro API, Piper can be used as a less natural back-up

### For agents, if you would like to remain 'free' to use.
- groq
  - llama-3.1-8b-instant
  - llama-3.3-70b-versatile
- gemini
  - Gemini 3 Flash
  - Gemini 3.1 Flash Lite
- ollama
  - phi4 mini
  - qwen
  - etc.
- hugging face inference api also has free tiers
  - e.g. moonshot
- if you have a paid agent service for API usage, you can add it through OpenRouter



1. Configure your AI agent services
   - Determine your AI service providers, create accounts, generate API keys
3. Configure a 'Room'
   - Setup name, topic
   - Add up to 4 agents
   - Give agents specific prompts
       - e.g. 'Creative Studio Room: 1 agent is a devil's advocate, 1 agent is an optimist, and 1 agent finds the common-ground'
       - 'DnD Room: 2 agents are players, 1 agent is the DM of a game of DnD'
       - 'Interview Room: 2 agents roleplay in a round-table interview style, asking the user questions'
       - so on and so forth
5. Go to the Chat, send an initial prompt (or empty) and let it go.
   - TTS is enabled, but STT is not implemented yet.
7. Intervene if the agents start drifting (work in progress)
8. View the shared room memory, agent memory, and logs in the Logs tab.
