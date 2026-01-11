# Space Shooter

A simple arcade-style space shooter demonstrating the BasicLang game framework.

## Features

- Player movement with WASD/Arrow keys
- Shooting with spacebar
- Enemy spawning and collision detection
- Score and lives system
- High score tracking
- Game states (Menu, Playing, Game Over)
- Player invincibility after being hit

## Controls

| Key | Action |
|-----|--------|
| W / Up Arrow | Move Up |
| S / Down Arrow | Move Down |
| A / Left Arrow | Move Left |
| D / Right Arrow | Move Right |
| Space | Shoot |
| Enter | Start Game |
| ESC | Return to Menu |

## Game Mechanics

- Enemies spawn from the top and move downward
- Destroy enemies by shooting them (+10 points)
- Avoid collisions with enemies (lose a life)
- 3 lives, with 2 seconds of invincibility after being hit
- Difficulty increases as score rises (faster enemy spawns)

## Building

Open `SpaceShooter.blproj` in Visual Game Studio and press F5 to build and run.

## Code Structure

The game demonstrates BasicLang features including:
- Integer constants for configuration
- Global variables for game state
- Select Case for state management
- Subroutines for code organization
- Integer math (positions scaled by 100 for precision)
- Simple pseudo-random number generation
- Collision detection with distance checks
