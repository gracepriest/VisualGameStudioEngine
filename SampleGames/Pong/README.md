# Pong

A classic two-player Pong game demonstrating simple game mechanics in BasicLang.

## Features

- **Two-Player Gameplay**: Local multiplayer with separate controls for each player
- **Ball Physics**: Ball bouncing with angle variation based on paddle hit position
- **Speed Increase**: Ball speeds up after each paddle hit
- **Score System**: First to 5 points wins
- **Game States**: Menu, Playing, Paused, Point Scored

## Controls

### Player 1 (Left Paddle)
| Key | Action |
|-----|--------|
| W | Move Up |
| S | Move Down |

### Player 2 (Right Paddle)
| Key | Action |
|-----|--------|
| Up Arrow | Move Up |
| Down Arrow | Move Down |

### General
| Key | Action |
|-----|--------|
| Enter/Space | Start Game |
| Space | Pause/Resume |
| ESC | Quit / Return to Menu |

## Game Mechanics

### Ball Behavior
- Ball starts at center with random direction
- Bounces off top and bottom walls
- Speed increases by 25 units per paddle hit (max 600)
- Angle changes based on where ball hits paddle

### Scoring
- Ball passing a paddle scores a point for the opponent
- Brief 1.5 second pause after each score
- Ball serves toward the player who just scored
- First to 5 points wins the game

## Code Structure

The game demonstrates these BasicLang features:
- Integer constants for game configuration
- Global variables for game state
- Select Case for state machine pattern
- Subroutines for code organization
- Integer math (positions scaled by 100 for precision)
- Game loop pattern (Update/Draw)
- Simple pseudo-random number generation

## Building

Open `Pong.blproj` in Visual Game Studio and press F5 to build and run.
