#pragma once

#include "MyMTKViewDelegate.hpp"

class MyAppDelegate : public NS::ApplicationDelegate {

public:
    MyAppDelegate() {

    }

    ~MyAppDelegate();

    void applicationWillFinishLaunching(NS::Notification* notification) override;
    void applicationDidFinishLaunching(NS::Notification* notification) override;
    bool applicationShouldTerminateAfterLastWindowClosed(NS::Application* sender) override;
private:

    NS::Menu* createMenuBar();

    NS::Window* _pWindow;
    MTK::View* _pMtkView;
    MTL::Device* _pDevice;
    MyMTKViewDelegate* _pViewDelegate;
};