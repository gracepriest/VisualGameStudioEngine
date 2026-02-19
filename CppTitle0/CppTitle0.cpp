#include <iostream>
#include "../VisualGameStudioEngine/framework.h"
#pragma comment(lib, "VisualGameStudioEngine.lib")

// ─── Sprite source rectangles ───
struct SpriteFrame { float x, y, w, h; };

// Big Mario
SpriteFrame MARIO_IDLE = { 172,  593, 20, 31 };

SpriteFrame MARIO_WALK[] = {
    { 172,  593, 20, 31 },   // #1 idle (part of walk cycle)
    { 93,  594, 19, 28 },   // #2 walk frame 1
    { 132, 592, 22, 31 },   // #3 walk frame 2
};
int WALK_FRAME_COUNT = 3;

SpriteFrame MARIO_JUMP = { 53, 593, 20, 31 };  // #4
SpriteFrame MARIO_SKID = { 252, 593, 20, 29 };  // #6
SpriteFrame MARIO_DEATH = { 15,  598, 16, 21 };  // #0 small mario

// ─── Game state ───
int playerEntity = -1;
int texHandle = -1;

float marioX = 100.0f;
float marioY = 400.0f;
float playerVelX = 0;
float playerVelY = 0;
bool onGround = false;
bool facingRight = true;

float animTimer = 0;
int   animFrame = 0;
float animSpeed = 0.1f;

const float GRAVITY = 980.0f;
const float MOVE_SPEED = 200.0f;
const float JUMP_FORCE = -450.0f;
const float SCALE = 2.0f;

float drawX;
float drawY;

void SetSpriteFrame(SpriteFrame& frame) {
    float srcW = facingRight ? -frame.w : frame.w;
    Framework_Ecs_SetSpriteSource(playerEntity, frame.x, frame.y, srcW, frame.h);
    Framework_Ecs_SetTransformScale(playerEntity, SCALE, SCALE);
    
  
		
    
    drawX = marioX;
    drawY = marioY - (frame.h * SCALE/2);
    Framework_Ecs_SetTransformPosition(playerEntity, drawX, drawY);
}

void GameDraw() {
    Framework_ClearBackground(92, 148, 252, 255);
    float dt = Framework_GetFrameTime();

    // ─── Input ───
    playerVelX = 0;
    if (Framework_IsKeyDown(263)) { playerVelX = -MOVE_SPEED; facingRight = false; }
    if (Framework_IsKeyDown(262)) { playerVelX =  MOVE_SPEED; facingRight = true;  }

    if (Framework_IsKeyPressed(32) && onGround) {
        playerVelY = JUMP_FORCE;
        onGround = false;
    }

    if (Framework_IsKeyReleased(32) && playerVelY < -100.0f) {
        playerVelY = -100.0f;
    }

    // ─── Physics ───
    playerVelY += GRAVITY * dt;
    marioX += playerVelX * dt;
    marioY += playerVelY * dt;

    if (marioY >= 400.0f) {
        marioY = 400.0f;
        playerVelY = 0;
        onGround = true;
    }

    // ─── Animation ───
    if (!onGround) {
        SetSpriteFrame(MARIO_JUMP);
    }
    else if (playerVelX != 0) {
        animTimer += dt;
        if (animTimer >= animSpeed) {
            animTimer -= animSpeed;
            animFrame = (animFrame + 1) % WALK_FRAME_COUNT;
        }
        SetSpriteFrame(MARIO_WALK[animFrame]);
    }
    else {
        animFrame = 0;
        animTimer = 0;
        SetSpriteFrame(MARIO_IDLE);
    }

    // ─── Render ───
    Framework_Camera_BeginMode();
    Framework_DrawRectangle(0, 432, 800, 48, 139, 69, 19, 255);
    Framework_Ecs_DrawSprites();
    Framework_Camera_EndMode();

    Framework_DrawText("Mario Clone - Arrow Keys + Space", 10, 10, 20, 255, 255, 255, 255);
}

int main() {
    Framework_Initialize(800, 480, "Mario Clone");
    Framework_SetTargetFPS(60);
    Framework_Camera_SetTarget(400, 240);

    playerEntity = Framework_Ecs_CreateEntity();
    Framework_Ecs_SetName(playerEntity, "Mario");
    Framework_Ecs_SetTag(playerEntity, "player");
    Framework_Ecs_AddTransform2D(playerEntity, marioX, marioY, 0, SCALE, SCALE);

    texHandle = Framework_AcquireTextureH("mario.png");
    Framework_Ecs_AddSprite2D(playerEntity, texHandle,
        MARIO_IDLE.x, MARIO_IDLE.y, MARIO_IDLE.w, MARIO_IDLE.h,
        255, 255, 255, 255,
        0);

    Framework_SetDrawCallback(GameDraw);

    while (!Framework_ShouldClose()) {
        Framework_Update();
    }

    Framework_ReleaseTextureH(texHandle);
    Framework_Shutdown();
    return 0;
}