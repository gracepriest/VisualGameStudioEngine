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
        private const int PADDLE_WIDTH = 15;
        private const int PADDLE_HEIGHT = 100;
        private const int PADDLE_SPEED = 400;
        private const int PADDLE_MARGIN = 30;
        private const int BALL_SIZE = 15;
        private const int BALL_INITIAL_SPEED = 300;
        private const int BALL_SPEED_INCREASE = 25;
        private const int BALL_MAX_SPEED = 600;
        private const int KEY_W = 87;
        private const int KEY_S = 83;
        private const int KEY_UP = 265;
        private const int KEY_DOWN = 264;
        private const int KEY_SPACE = 32;
        private const int KEY_ESC = 256;
        private const int KEY_ENTER = 257;
        private const int STATE_MENU = 0;
        private const int STATE_PLAYING = 1;
        private const int STATE_PAUSED = 2;
        private const int STATE_POINT = 3;
        private const int WIN_SCORE = 5;

        // Global variables
        private static int gameState = STATE_MENU;
        private static int paddle1Y = 25000;
        private static int paddle2Y = 25000;
        private static int ballX = 40000;
        private static int ballY = 30000;
        private static int ballVelX = 0;
        private static int ballVelY = 0;
        private static int ballSpeed = 30000;
        private static int score1 = 0;
        private static int score2 = 0;
        private static int pointTimer = 0;
        private static int lastScorer = 0;
        private static int randomSeed = 54321;

        public static void Main()
        {
            FrameworkWrapper.Framework_Initialize(SCREEN_WIDTH, SCREEN_HEIGHT, "Pong");
            while (!FrameworkWrapper.Framework_ShouldClose())
            {
                switch (gameState)
                {
                    case STATE_MENU:
                        UpdateMenu();
                        DrawMenu();
                        break;
                    case STATE_PLAYING:
                        UpdatePlaying();
                        DrawGame();
                        break;
                    case STATE_PAUSED:
                        UpdatePaused();
                        DrawPaused();
                        break;
                    case STATE_POINT:
                        UpdatePoint();
                        DrawGame();
                        break;
                    default:
                        break;
                }
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
            FrameworkWrapper.Framework_ClearBackground((byte)(0), (byte)(0), (byte)(0), 255);
            FrameworkWrapper.Framework_DrawText("PONG", 320, 150, 72, (byte)(255), (byte)(255), (byte)(255), (byte)(255));
            FrameworkWrapper.Framework_DrawText("Press ENTER or SPACE to Start", 240, 300, 24, (byte)(200), (byte)(200), (byte)(200), (byte)(255));
            FrameworkWrapper.Framework_DrawText("Player 1: W/S", 100, 400, 20, (byte)(100), (byte)(200), (byte)(255), (byte)(255));
            FrameworkWrapper.Framework_DrawText("Player 2: Up/Down", 550, 400, 20, (byte)(255), (byte)(200), (byte)(100), (byte)(255));
            FrameworkWrapper.Framework_DrawText("First to 5 wins!", 320, 500, 20, (byte)(255), (byte)(255), (byte)(100), (byte)(255));
            FrameworkWrapper.Framework_EndDrawing();
        }

        public static void ResetGame()
        {
            score1 = 0;
            score2 = 0;
            paddle1Y = ((SCREEN_HEIGHT / 2) - (PADDLE_HEIGHT / 2)) * 100;
            paddle2Y = ((SCREEN_HEIGHT / 2) - (PADDLE_HEIGHT / 2)) * 100;
            ResetBall(0);
        }

        public static void ResetBall(int direction)
        {
            int randVal = 0;
            int remainder = 0;
            int angleOffset = 0;

            ballX = ((SCREEN_WIDTH / 2) - (BALL_SIZE / 2)) * 100;
            ballY = ((SCREEN_HEIGHT / 2) - (BALL_SIZE / 2)) * 100;
            ballSpeed = BALL_INITIAL_SPEED * 100;
            randomSeed = (randomSeed * 1103515245) + 12345;
            randVal = randomSeed;
            if (randomSeed < 0)
            {
                randVal = -randVal;
            }
            remainder = randVal % 100;
            angleOffset = remainder - 50;
            if (direction == 0)
            {
                randomSeed = (randomSeed * 1103515245) + 12345;
                randVal = randomSeed;
                if (randomSeed < 0)
                {
                    randVal = -randVal;
                }
                remainder = randVal % 2;
                if (remainder == 0)
                {
                    ballVelX = ballSpeed;
                }
                else
                {
                    ballVelX = -ballSpeed;
                }
            }
            else
            {
                if (direction == 1)
                {
                    ballVelX = ballSpeed;
                }
                else
                {
                    ballVelX = -ballSpeed;
                }
            }
            ballVelY = (ballSpeed * angleOffset) / 200;
        }

        public static void UpdatePlaying()
        {
            if (FrameworkWrapper.Framework_IsKeyPressed(KEY_SPACE) || FrameworkWrapper.Framework_IsKeyPressed(KEY_ESC))
            {
                gameState = STATE_PAUSED;
            }
            UpdatePaddles();
            UpdateBall();
        }

        public static void UpdatePaddles()
        {
            int maxY = 0;

            if (FrameworkWrapper.Framework_IsKeyDown(KEY_W))
            {
                paddle1Y = paddle1Y - PADDLE_SPEED;
            }
            if (FrameworkWrapper.Framework_IsKeyDown(KEY_S))
            {
                paddle1Y = paddle1Y + PADDLE_SPEED;
            }
            if (FrameworkWrapper.Framework_IsKeyDown(KEY_UP))
            {
                paddle2Y = paddle2Y - PADDLE_SPEED;
            }
            if (FrameworkWrapper.Framework_IsKeyDown(KEY_DOWN))
            {
                paddle2Y = paddle2Y + PADDLE_SPEED;
            }
            if (paddle1Y < 0)
            {
                paddle1Y = 0;
            }
            maxY = (SCREEN_HEIGHT - PADDLE_HEIGHT) * 100;
            if (paddle1Y > maxY)
            {
                paddle1Y = maxY;
            }
            if (paddle2Y < 0)
            {
                paddle2Y = 0;
            }
            if (paddle2Y > maxY)
            {
                paddle2Y = maxY;
            }
        }

        public static void UpdateBall()
        {
            int maxBallY = 0;
            int bx = 0;
            int by = 0;
            int p1y = 0;
            int p2y = 0;
            int hitPos = 0;
            int rightPaddleX = 0;

            ballX = ballX + (ballVelX / 60);
            ballY = ballY + (ballVelY / 60);
            if (ballY <= 0)
            {
                ballY = 0;
                ballVelY = -ballVelY;
            }
            maxBallY = (SCREEN_HEIGHT - BALL_SIZE) * 100;
            if (ballY >= maxBallY)
            {
                ballY = maxBallY;
                ballVelY = -ballVelY;
            }
            bx = ballX / 100;
            by = ballY / 100;
            p1y = paddle1Y / 100;
            p2y = paddle2Y / 100;
            if (bx <= (PADDLE_MARGIN + PADDLE_WIDTH))
            {
                if (((by + BALL_SIZE) >= p1y) && (by <= (p1y + PADDLE_HEIGHT)))
                {
                    ballX = (PADDLE_MARGIN + PADDLE_WIDTH) * 100;
                    ballVelX = -ballVelX;
                    hitPos = (((by + (BALL_SIZE / 2)) - p1y) * 100) / PADDLE_HEIGHT;
                    ballVelY = (ballSpeed * (hitPos - 50)) / 100;
                    IncreaseBallSpeed();
                }
            }
            rightPaddleX = (SCREEN_WIDTH - PADDLE_MARGIN) - PADDLE_WIDTH;
            if ((bx + BALL_SIZE) >= rightPaddleX)
            {
                if (((by + BALL_SIZE) >= p2y) && (by <= (p2y + PADDLE_HEIGHT)))
                {
                    ballX = (rightPaddleX - BALL_SIZE) * 100;
                    ballVelX = -ballVelX;
                    hitPos = (((by + (BALL_SIZE / 2)) - p2y) * 100) / PADDLE_HEIGHT;
                    ballVelY = (ballSpeed * (hitPos - 50)) / 100;
                    IncreaseBallSpeed();
                }
            }
            if (bx < (-BALL_SIZE))
            {
                score2 = score2 + 1;
                lastScorer = 2;
                CheckWin();
            }
            else
            {
                if (bx > SCREEN_WIDTH)
                {
                    score1 = score1 + 1;
                    lastScorer = 1;
                    CheckWin();
                }
                else
                {
                }
            }
        }

        public static void IncreaseBallSpeed()
        {
            int maxSpeed = 0;

            ballSpeed = ballSpeed + (BALL_SPEED_INCREASE * 100);
            maxSpeed = BALL_MAX_SPEED * 100;
            if (ballSpeed > maxSpeed)
            {
                ballSpeed = maxSpeed;
            }
        }

        public static void CheckWin()
        {
            if ((score1 >= WIN_SCORE) || (score2 >= WIN_SCORE))
            {
                gameState = STATE_MENU;
            }
            else
            {
                pointTimer = 90;
                gameState = STATE_POINT;
            }
        }

        public static void UpdatePoint()
        {
            pointTimer = pointTimer - 1;
            if (pointTimer <= 0)
            {
                ResetBall(lastScorer);
                gameState = STATE_PLAYING;
            }
        }

        public static void UpdatePaused()
        {
            if (FrameworkWrapper.Framework_IsKeyPressed(KEY_SPACE) || FrameworkWrapper.Framework_IsKeyPressed(KEY_ENTER))
            {
                gameState = STATE_PLAYING;
            }
            if (FrameworkWrapper.Framework_IsKeyPressed(KEY_ESC))
            {
                gameState = STATE_MENU;
            }
        }

        public static void DrawPaused()
        {
            FrameworkWrapper.Framework_BeginDrawing();
            FrameworkWrapper.Framework_ClearBackground((byte)(0), (byte)(0), (byte)(0), 255);
            DrawGameElements();
            FrameworkWrapper.Framework_DrawRectangle(0, 0, SCREEN_WIDTH, SCREEN_HEIGHT, (byte)(0), (byte)(0), (byte)(0), (byte)(150));
            FrameworkWrapper.Framework_DrawText("PAUSED", 320, 250, 48, (byte)(255), (byte)(255), (byte)(255), (byte)(255));
            FrameworkWrapper.Framework_DrawText("Press SPACE to Resume", 280, 320, 24, (byte)(200), (byte)(200), (byte)(200), (byte)(255));
            FrameworkWrapper.Framework_EndDrawing();
        }

        public static void DrawGame()
        {
            FrameworkWrapper.Framework_BeginDrawing();
            FrameworkWrapper.Framework_ClearBackground((byte)(0), (byte)(0), (byte)(0), 255);
            DrawGameElements();
            if (gameState == STATE_POINT)
            {
                if (lastScorer == 1)
                {
                    FrameworkWrapper.Framework_DrawText("Player 1 Scores!", 280, 350, 32, (byte)(100), (byte)(200), (byte)(255), (byte)(255));
                }
                else
                {
                    FrameworkWrapper.Framework_DrawText("Player 2 Scores!", 280, 350, 32, (byte)(255), (byte)(200), (byte)(100), (byte)(255));
                }
            }
            FrameworkWrapper.Framework_EndDrawing();
        }

        public static void DrawGameElements()
        {
            int i = 0;
            int p1y = 0;
            int p2y = 0;
            int bx = 0;
            int by = 0;

            i = 0;
            while (i <= SCREEN_HEIGHT)
            {
                FrameworkWrapper.Framework_DrawRectangle((SCREEN_WIDTH / 2) - 2, i, 4, 15, (byte)(100), (byte)(100), (byte)(100), (byte)(255));
                i = i + 30;
            }
            FrameworkWrapper.Framework_DrawText("" + score1, (SCREEN_WIDTH / 4) - 20, 30, 64, (byte)(100), (byte)(200), (byte)(255), (byte)(255));
            FrameworkWrapper.Framework_DrawText("" + score2, ((SCREEN_WIDTH * 3) / 4) - 20, 30, 64, (byte)(255), (byte)(200), (byte)(100), (byte)(255));
            p1y = paddle1Y / 100;
            p2y = paddle2Y / 100;
            FrameworkWrapper.Framework_DrawRectangle(PADDLE_MARGIN, p1y, PADDLE_WIDTH, PADDLE_HEIGHT, (byte)(100), (byte)(200), (byte)(255), (byte)(255));
            FrameworkWrapper.Framework_DrawRectangle((SCREEN_WIDTH - PADDLE_MARGIN) - PADDLE_WIDTH, p2y, PADDLE_WIDTH, PADDLE_HEIGHT, (byte)(255), (byte)(200), (byte)(100), (byte)(255));
            bx = ballX / 100;
            by = ballY / 100;
            FrameworkWrapper.Framework_DrawRectangle(bx, by, BALL_SIZE, BALL_SIZE, (byte)(255), (byte)(255), (byte)(255), (byte)(255));
        }

    }

}

