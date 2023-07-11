#pragma once

#include "Renderer.hpp"

class MyMTKViewDelegate : public MTK::ViewDelegate {

public:

    MyMTKViewDelegate(MTL::Device* pDevice);
    ~MyMTKViewDelegate();

    void drawInMTKView( MTK::View* pView ) override;

private:
    Renderer* _pRenderer;
};