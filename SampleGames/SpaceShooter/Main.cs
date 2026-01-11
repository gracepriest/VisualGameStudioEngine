using RaylibWrapper;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public static class Program
    {
        // Constants
        private const int SCREEN_WIDTH = 800;
        private const int SCREEN_HEIGHT = 600;
        private const int PLAYER_SIZE = 30;
        private const int PLAYER_SPEED = 300;
        private const int BULLET_SIZE = 8;
        private const int BULLET_SPEED = 500;
        private const int ENEMY_SIZE = 30;
        private const int ENEMY_SPEED = 150;
        private const int KEY_W = 87;
        private const int KEY_S = 83;
        private const int KEY_A = 65;
        private const int KEY_D = 68;
        private const int KEY_UP = 265;
        private const int KEY_DOWN = 264;
        private const int KEY_LEFT = 263;
        private const int KEY_RIGHT = 262;
        private const int KEY_SPACE = 32;
        private const int KEY_ESC = 256;
        private const int KEY_ENTER = 257;
        private const int STATE_MENU = 0;
        private const int STATE_PLAYING = 1;
        private const int STATE_GAMEOVER = 2;

        // Global variables
        private static int gameState = STATE_MENU;
        private static int playerX = 38000;
        private static int playerY = 50000;
        private static int score = 0;
        private static int highScore = 0;
        private static int lives = 3;
        private static int shootCooldown = 0;
        private static int bulletX = -1;
        private static int bulletY = 0;
        private static int enemyX = -1;
        private static int enemyY = 0;
        private static int spawnTimer = 0;
        private static int invincibleTimer = 0;
        private static int randomSeed = 12345;

        public static void Main()
        {
            FrameworkWrapper.Framework_Initialize(SCREEN_WIDTH, SCREEN_HEIGHT, "Space Shooter");
            while (!FrameworkWrapper.Framework_ShouldClose())
            {
            }
            FrameworkWrapper.Framework_Shutdown();
        }

        public static void UpdateMenu()
        {
            if (FrameworkWrapper.Framework_IsKeyPressed(KEY_ENTER) || FrameworkWrapper.Framework_IsKeyPressed(KEY_SPACE))
            {
                ResetGame();
                gameState = STATE_PLAYING;
            }
            if (FrameworkWrapper.Framework_IsKeyPressed(KEY_ESC))
            {
                FrameworkWrapper.Framework_Shutdown();
            }
        }

        public static void DrawMenu()
        {
            FrameworkWrapper.Framework_BeginDrawing();
            FrameworkWrapper.Framework_ClearBackground((byte)(10), (byte)(10), (byte)(30), 255);
            FrameworkWrapper.Framework_DrawText("SPACE SHOOTER", 250, 150, 48, (byte)(255), (byte)(255), (byte)(255), (byte)(255));
            FrameworkWrapper.Framework_DrawText("Press ENTER to Start", 280, 300, 24, (byte)(200), (byte)(200), (byte)(200), (byte)(255));
            FrameworkWrapper.Framework_DrawText("WASD/Arrows: Move", 300, 380, 20, (byte)(150), (byte)(150), (byte)(150), (byte)(255));
            FrameworkWrapper.Framework_DrawText("SPACE: Shoot", 320, 410, 20, (byte)(150), (byte)(150), (byte)(150), (byte)(255));
            FrameworkWrapper.Framework_DrawText("High Score: " + highScore, 320, 480, 24, (byte)(255), (byte)(255), (byte)(100), (byte)(255));
            FrameworkWrapper.Framework_EndDrawing();
        }

        public static void ResetGame()
        {
            playerX = SCREEN_WIDTH / 2 * 100;
            playerY = SCREEN_HEIGHT - 80 * 100;
            score = 0;
            lives = 3;
            shootCooldown = 0;
            invincibleTimer = 0;
            spawnTimer = 60;
            bulletX = -1;
            enemyX = -1;
        }

        public static void UpdatePlaying()
        {
            int randVal = 0;
            int remainder = 0;

            if (FrameworkWrapper.Framework_IsKeyPressed(KEY_ESC))
            {
                gameState = STATE_MENU;
            }
            if (FrameworkWrapper.Framework_IsKeyDown(KEY_W) || FrameworkWrapper.Framework_IsKeyDown(KEY_UP))
            {
                playerY = playerY - PLAYER_SPEED;
            }
            if (FrameworkWrapper.Framework_IsKeyDown(KEY_S) || FrameworkWrapper.Framework_IsKeyDown(KEY_DOWN))
            {
                playerY = playerY + PLAYER_SPEED;
            }
            if (FrameworkWrapper.Framework_IsKeyDown(KEY_A) || FrameworkWrapper.Framework_IsKeyDown(KEY_LEFT))
            {
                playerX = playerX - PLAYER_SPEED;
            }
            if (FrameworkWrapper.Framework_IsKeyDown(KEY_D) || FrameworkWrapper.Framework_IsKeyDown(KEY_RIGHT))
            {
                playerX = playerX + PLAYER_SPEED;
            }
            if (playerX < 0)
            {
                playerX = 0;
            }
            if (playerX > ((SCREEN_WIDTH - PLAYER_SIZE) * 100))
            {
                playerX = SCREEN_WIDTH - PLAYER_SIZE * 100;
            }
            if (playerY < 0)
            {
                playerY = 0;
            }
            if (playerY > ((SCREEN_HEIGHT - PLAYER_SIZE) * 100))
            {
                playerY = SCREEN_HEIGHT - PLAYER_SIZE * 100;
            }
            if ((FrameworkWrapper.Framework_IsKeyDown(KEY_SPACE) && (shootCooldown <= 0)) && (bulletX < 0))
            {
                bulletX = playerX + (PLAYER_SIZE / 2) * 100;
                bulletY = playerY;
                shootCooldown = 20;
            }
            if (bulletX >= 0)
            {
                bulletY = bulletY - (BULLET_SPEED * 100) / 60;
                if (bulletY < 0)
                {
                    bulletX = -1;
                }
            }
            spawnTimer = spawnTimer - 1;
            if ((spawnTimer <= 0) && (enemyX < 0))
            {
                randomSeed = randomSeed * 1103515245 + 12345;
                randVal = randomSeed;
                if (randomSeed < 0)
                {
                    randVal = -randVal;
                }
                remainder = randVal;
                Mod(SCREEN_WIDTH - ENEMY_SIZE);
                enemyX = randVal * 100;
                enemyY = -ENEMY_SIZE * 100;
                spawnTimer = 90 - score / 10;
                if (spawnTimer < 40)
                {
                    spawnTimer = 40;
                }
            }
            if (enemyX >= 0)
            {
                enemyY = enemyY + (ENEMY_SPEED * 100) / 60;
                if (enemyY > (SCREEN_HEIGHT * 100))
                {
                    enemyX = -1;
                }
            }
            CheckCollisions();
            if (shootCooldown > 0)
            {
                shootCooldown = shootCooldown - 1;
            }
            if (invincibleTimer > 0)
            {
                invincibleTimer = invincibleTimer - 1;
            }
        }

        public static void CheckCollisions()
        {
            int bx = 0;
            int by = 0;
            int ex = 0;
            int ey = 0;
            int dx = 0;
            int dy = 0;
            int px = 0;
            int py = 0;

            if ((bulletX >= 0) && (enemyX >= 0))
            {
                bx = bulletX / 100;
                by = bulletY / 100;
                ex = enemyX / 100;
                ey = enemyY / 100;
                dx = bx - ex;
                if (dx < 0)
                {
                    dx = -dx;
                }
                dy = by - ey;
                if (dy < 0)
                {
                    dy = -dy;
                }
                if ((dx < ENEMY_SIZE) && (dy < ENEMY_SIZE))
                {
                    enemyX = -1;
                    bulletX = -1;
                    score = score + 10;
                }
            }
            if ((invincibleTimer <= 0) && (enemyX >= 0))
            {
                px = playerX / 100;
                py = playerY / 100;
                ex = enemyX / 100;
                ey = enemyY / 100;
                dx = px - ex;
                if (dx < 0)
                {
                    dx = -dx;
                }
                dy = py - ey;
                if (dy < 0)
                {
                    dy = -dy;
                }
                if ((dx < ((PLAYER_SIZE + ENEMY_SIZE) / 2)) && (dy < ((PLAYER_SIZE + ENEMY_SIZE) / 2)))
                {
                    enemyX = -1;
                    lives = lives - 1;
                    invincibleTimer = 120;
                    if (lives <= 0)
                    {
                        if (score > highScore)
                        {
                            highScore = score;
                        }
                        gameState = STATE_GAMEOVER;
                    }
                }
            }
        }

        public static void UpdateGameOver()
        {
            if (FrameworkWrapper.Framework_IsKeyPressed(KEY_ENTER) || FrameworkWrapper.Framework_IsKeyPressed(KEY_SPACE))
            {
                gameState = STATE_MENU;
            }
        }

        public static void DrawGameOver()
        {
            FrameworkWrapper.Framework_BeginDrawing();
            FrameworkWrapper.Framework_ClearBackground((byte)(10), (byte)(10), (byte)(30), 255);
            FrameworkWrapper.Framework_DrawText("GAME OVER", 280, 200, 48, (byte)(255), (byte)(100), (byte)(100), (byte)(255));
            FrameworkWrapper.Framework_DrawText("Score: " + score, 340, 300, 32, (byte)(255), (byte)(255), (byte)(255), (byte)(255));
            FrameworkWrapper.Framework_DrawText("High Score: " + highScore, 300, 360, 24, (byte)(255), (byte)(255), (byte)(100), (byte)(255));
            FrameworkWrapper.Framework_DrawText("Press ENTER", 320, 450, 24, (byte)(200), (byte)(200), (byte)(200), (byte)(255));
            FrameworkWrapper.Framework_EndDrawing();
        }

        public static void DrawGame()
        {
            int ex = 0;
            int ey = 0;
            bool showPlayer = false;
            int blinkMod = 0;
            int px = 0;
            int py = 0;

            FrameworkWrapper.Framework_BeginDrawing();
            FrameworkWrapper.Framework_ClearBackground((byte)(10), (byte)(10), (byte)(30), 255);
            FrameworkWrapper.Framework_DrawRectangle(100, 50, 2, 2, (byte)(255), (byte)(255), (byte)(255), (byte)(200));
            FrameworkWrapper.Framework_DrawRectangle(300, 120, 2, 2, (byte)(255), (byte)(255), (byte)(255), (byte)(150));
            FrameworkWrapper.Framework_DrawRectangle(500, 80, 2, 2, (byte)(255), (byte)(255), (byte)(255), (byte)(200));
            FrameworkWrapper.Framework_DrawRectangle(700, 200, 2, 2, (byte)(255), (byte)(255), (byte)(255), (byte)(180));
            FrameworkWrapper.Framework_DrawRectangle(150, 300, 2, 2, (byte)(255), (byte)(255), (byte)(255), (byte)(150));
            FrameworkWrapper.Framework_DrawRectangle(450, 350, 2, 2, (byte)(255), (byte)(255), (byte)(255), (byte)(200));
            if (enemyX >= 0)
            {
                ex = enemyX / 100;
                ey = enemyY / 100;
                FrameworkWrapper.Framework_DrawRectangle(ex, ey, ENEMY_SIZE, ENEMY_SIZE, (byte)(255), (byte)(100), (byte)(100), (byte)(255));
            }
            if (bulletX >= 0)
            {
                FrameworkWrapper.Framework_DrawRectangle(bulletX / 100, bulletY / 100, BULLET_SIZE, BULLET_SIZE, (byte)(255), (byte)(255), (byte)(100), (byte)(255));
            }
            showPlayer = true;
            if (invincibleTimer > 0)
            {
                blinkMod = invincibleTimer;
                Mod(10);
                if (invincibleTimer < 5)
                {
                    showPlayer = false;
                }
            }
            if (showPlayer)
            {
                px = playerX / 100;
                py = playerY / 100;
                FrameworkWrapper.Framework_DrawRectangle(px, py, PLAYER_SIZE, PLAYER_SIZE, (byte)(100), (byte)(200), (byte)(255), (byte)(255));
            }
            FrameworkWrapper.Framework_DrawText("SCORE: " + score, 20, 20, 24, (byte)(255), (byte)(255), (byte)(255), (byte)(255));
            FrameworkWrapper.Framework_DrawText("LIVES: " + lives, 650, 20, 24, (byte)(255), (byte)(100), (byte)(100), (byte)(255));
            FrameworkWrapper.Framework_EndDrawing();
        }

    }

}

