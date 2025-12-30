# NationsCities 🌍

**NationsCities** is a modern, real-time multiplayer web adaptation of the classic paper-and-pencil game **"Państwa-Miasta"** (known internationally as *Scattergories* or *Categories*). Built with **.NET Blazor Server** and **SignalR**, it offers a seamless, interactive gaming experience for friends to play together online.

## ✨ Features

- **Real-Time Multiplayer**: Instant state synchronization across all clients using SignalR.
- **Dynamic Lobbies**: Create private rooms for friends or public rooms for anyone to join.
- **Customizable Gameplay**: extensive category selection system with support for custom categories.
- **Interactive Voting**: Unique voting phase where players validate each other's answers to ensure fairness.
- **Live Scoreboard**: Real-time point tracking after every round.
- **Responsive Design**: Polished, mobile-friendly UI using modern CSS.

## 🛠️ Tech Stack

- **Framework**: [.NET 10.0](https://dotnet.microsoft.com/) (Blazor Server)
- **Real-time Communication**: [SignalR](https://dotnet.microsoft.com/apps/aspnet/signalr)
- **Language**: C#
- **Styling**: Vanilla CSS (Modern, Responsive)

## 🚀 Getting Started

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) (or latest separate preview/release)

### Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/seruss/NationsCities.git
   cd NationsCities
   ```

2. **Restore dependencies**
   ```bash
   dotnet restore
   ```

3. **Run the application**
   ```bash
   dotnet watch run
   ```

4. **Play!**
   Open your browser and navigate to `http://localhost:5229` (or the port indicated in your terminal).

## 📖 How to Play

1. **Create a Room**: Enter a nickname and start a new lobby.
2. **Invite Friends**: Share the room code or make it public.
3. **Select Categories**: The host chooses which categories (e.g., Countries, Cities, Animals) to play with.
4. **Game Round**: A random letter is drawn. Players race to type words starting with that letter for each category.
5. **Validation**: Players review each other's answers. Vote to accept or reject answers.
6. **Win**: Points are tallied, and the player with the highest score wins!

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
