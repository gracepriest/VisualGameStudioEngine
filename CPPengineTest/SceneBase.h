#pragma once
#include "framework.h"
#include <cassert>

//base class for scenes
class Scene
{	
private:
	int sceneID =-1; // ID assigned by the framework
	
	
	public:
	virtual void OnEnter() = 0;
	virtual void OnExit() = 0;
	virtual void OnResume() = 0;
	virtual void OnUpdateFixed(double dt) = 0;   // fixed step
	virtual void OnUpdateFrame(float dt) = 0;    // per-frame
	virtual void OnDraw() = 0;
	virtual ~Scene() {}
};



static Scene* gCurrent = nullptr;

static void CB_OnEnter() { if (gCurrent) gCurrent->OnEnter(); }
static void CB_OnExit() { if (gCurrent) gCurrent->OnExit(); }
static void CB_OnResume() { if (gCurrent) gCurrent->OnResume(); }
static void CB_OnUpdateFixed(double dt) { if (gCurrent) gCurrent->OnUpdateFixed(dt); }
static void CB_OnUpdateFrame(float dt) { if (gCurrent) gCurrent->OnUpdateFrame(dt); }
static void CB_OnDraw() { if (gCurrent) gCurrent->OnDraw(); }
static void EngineDraw() { Framework_SceneTick(); } // your draw bridge

static SceneCallbacks MakeCallbacks() {
    SceneCallbacks cb{};
    cb.onEnter = &CB_OnEnter;
    cb.onExit = &CB_OnExit;
    cb.onResume = &CB_OnResume;
    cb.onUpdateFixed = &CB_OnUpdateFixed;
    cb.onUpdateFrame = &CB_OnUpdateFrame;
    cb.onDraw = &CB_OnDraw;
    return cb;
}

// Call this to switch scenes
inline int SetCurrentScene(Scene* scene) {
    gCurrent = scene;
    const auto cb = MakeCallbacks();
    const int handle = Framework_CreateScriptScene(cb);
    assert(handle >= 0);
    Framework_SceneChange(handle);
    return handle;
}

inline void WireEngineDraw() {
    Framework_SetDrawCallback(&EngineDraw);
}