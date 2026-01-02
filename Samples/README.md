# Sample Games

This directory contains sample games demonstrating Visual Game Studio Engine features.

## Games

### Pong
Classic two-player Pong game.
- **Controls**: Player 1: W/S, Player 2: Up/Down
- **Features**: Ball physics, paddle collision, score tracking

### Space Shooter
Arcade-style space shooter.
- **Controls**: Arrow keys or A/D to move, SPACE to shoot
- **Features**: Bullet spawning, enemy waves, collision detection

### Platformer
Simple platform game with collectibles.
- **Controls**: Arrow keys or WASD to move, SPACE to jump
- **Features**: Gravity physics, tile-based collision, one-way platforms

## Running Samples

1. Open Visual Game Studio IDE
2. File > Open Project
3. Navigate to the sample folder
4. Press F5 to run

## Learning Path

1. Start with **Pong** - basic game loop and input handling
2. Move to **Space Shooter** - arrays and game objects
3. Finish with **Platformer** - physics and tile-based levels

## Creating Your Own

Use these samples as templates for your own games:

```vb
' Basic game structure
Sub Main()
    Framework_Initialize(800, 600, "My Game")

    While Not Framework_ShouldClose()
        Update()
        Draw()
    End While

    Framework_Shutdown()
End Sub
```
