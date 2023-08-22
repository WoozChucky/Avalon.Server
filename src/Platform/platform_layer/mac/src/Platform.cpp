#include <iostream>
#include "Platform.h"

#define NS_PRIVATE_IMPLEMENTATION
#define MTL_PRIVATE_IMPLEMENTATION
#define MTK_PRIVATE_IMPLEMENTATION
#define CA_PRIVATE_IMPLEMENTATION

#include <Foundation/Foundation.hpp>
#include <Metal/Metal.hpp>
#include <QuartzCore/QuartzCore.hpp>
#include <AppKit/AppKit.hpp>
#include <MetalKit/MetalKit.hpp>



#include "MyAppDelegate.hpp"
#include "MyMTKViewDelegate.hpp"
#include "Renderer.hpp"

void Initialize()
{
    // Initialize the platform layer
    std::cout << "Initializing the platform layer" << std::endl;

    NS::AutoreleasePool* pAutoreleasePool = NS::AutoreleasePool::alloc()->init();

    const MyAppDelegate del;

    NS::Application* pSharedApplication = NS::Application::sharedApplication();
    pSharedApplication->setDelegate( &del );
    pSharedApplication->run();

    pAutoreleasePool->release();
}
