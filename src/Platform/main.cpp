#include <iostream>

#include "Platform.h"

#if !defined(__FMA__) && defined(__AVX2__)
#define __FMA__ 1
#endif

#include <glad/glad.h>
#include <GLFW/glfw3.h>
#include <stb_image/stb_image.h>
#include <stb_truetype/stb_truetype.h>
#include "gl2d/gl2d.h"
#include <raudio.h>

#define REMOVE_IMGUI 0

#if REMOVE_IMGUI == 0
#include "imgui.h"
#include "backends/imgui_impl_glfw.h"
#include "backends/imgui_impl_opengl3.h"
#include "imguiThemes.h"
#endif

int main() {
    std::cout << "Hello, World!" << std::endl;

    //Initialize();

    glfwInit();

    glfwWindowHint(GLFW_SAMPLES, 4);

    auto wind = glfwCreateWindow(600, 400, "", nullptr, nullptr);
    glfwMakeContextCurrent(wind);
    glfwSwapInterval(1);

    gladLoadGLLoader((GLADloadproc) glfwGetProcAddress);

    gl2d::init();

#if REMOVE_IMGUI == 0
    ImGui::CreateContext();
    //ImGui::StyleColorsDark();
    imguiThemes::embraceTheDarkness();

    ImGuiIO& io = ImGui::GetIO(); (void)io;
    io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;
    //io.ConfigFlags |= ImGuiConfigFlags_NavEnableGamepad;      // Enable Gamepad Controls
    io.ConfigFlags |= ImGuiConfigFlags_DockingEnable;           // Enable Docking
    io.ConfigFlags |= ImGuiConfigFlags_ViewportsEnable;         // Enable Multi-Viewport / Platform Windows
    //io.ConfigViewportsNoAutoMerge = true;
    //io.ConfigViewportsNoTaskBarIcon = true;

    ImGuiStyle& style = ImGui::GetStyle();
    if (io.ConfigFlags & ImGuiConfigFlags_ViewportsEnable)
    {
        //style.WindowRounding = 0.0f;
        style.Colors[ImGuiCol_WindowBg].w = 0.f;
        style.Colors[ImGuiCol_DockingEmptyBg].w = 0.f;
    }

    ImGui_ImplGlfw_InitForOpenGL(wind, true);
    ImGui_ImplOpenGL3_Init("#version 330");
#endif

    while (!glfwWindowShouldClose(wind)) {

#if REMOVE_IMGUI == 0
        ImGui_ImplOpenGL3_NewFrame();
        ImGui_ImplGlfw_NewFrame();
        ImGui::NewFrame();
        ImGui::DockSpaceOverViewport(ImGui::GetMainViewport());
#endif

        glfwPollEvents();

        glClearColor(0.2f, 0.2f, 0.2f, 1.f);
        glClear(GL_COLOR_BUFFER_BIT);




#if REMOVE_IMGUI == 0
        ImGui::Render();
        int display_w, display_h;
        glfwGetFramebufferSize(wind, &display_w, &display_h);
        glViewport(0, 0, display_w, display_h);
        ImGui_ImplOpenGL3_RenderDrawData(ImGui::GetDrawData());

        // Update and Render additional Platform Windows
        // (Platform functions may change the current OpenGL context, so we save/restore it to make it easier to paste this code elsewhere.
        //  For this specific demo app we could also call glfwMakeContextCurrent(window) directly)
        if (io.ConfigFlags & ImGuiConfigFlags_ViewportsEnable)
        {
            GLFWwindow* backup_current_context = glfwGetCurrentContext();
            ImGui::UpdatePlatformWindows();
            ImGui::RenderPlatformWindowsDefault();
            glfwMakeContextCurrent(backup_current_context);
        }
#endif

        glfwSwapBuffers(wind);
    }

    return 0;
}
