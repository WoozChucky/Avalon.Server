#pragma once

struct KeyEvent {
    int key;
};

typedef void (*GameUpdateFunction)(double deltaTime);
typedef void (*GameRenderFunction)();

typedef struct GameLayer {
    GameUpdateFunction update;
    GameRenderFunction render;
    double loadTime;
} GameLayer;

bool IsGameLayerModified();

