#pragma once

struct KeyEvent {
    int key;
};

typedef void (*GameUpdateFunction)();
typedef void (*GameRenderFunction)();

struct GameLayer {
    GameUpdateFunction update;
    GameRenderFunction render;
} GameLayer;

