# 🎮 MultiClicker - Dofus Multi-Account Manager

**MultiClicker** is a comprehensive Windows application designed for efficient Dofus multi-account management. This tool provides advanced window management, automation capabilities, and customization options to streamline your multi-character gaming experience.

## ✨ Key Features

### 🖥️ **Intelligent Window Management**
- **Automatic Window Detection**: Detects and manages all Dofus windows automatically
- **Smart Panel System**: Individual customizable panels for each window with visual feedback
- **Drag & Drop Reordering**: Intuitive panel rearrangement
- **Visual Selection Indicators**: Clear selection highlighting with distinctive borders

### ⚡ **Advanced Click Automation**
- **Multiple Click Types**: Support for single clicks, double clicks, and instant no-delay clicks
- **Precise Position Targeting**: Configurable click positions for HDV operations and other actions
- **Smart Delay System**: Customizable timing delays for natural automation behavior
- **OCR Integration**: Text recognition capabilities for enhanced automation workflows

### 🎨 **Customization Options**
- **Custom Panel Backgrounds**: Personalize character panels with custom themes
- **Built-in Cosmetics**: Pre-configured themes for various character types
- **Dynamic Image Caching**: Optimized image loading for improved performance
- **Color Themes**: Support for multiple character color schemes

### 🌐 **Multi-Language Support**
- **English/French/Spanish**: Complete interface localization
- **Automatic Language Detection**: Detects system language on startup  
- **One-Click Language Switching**: Change language using the 🌐 button
- **Persistent Language Settings**: Preference saved between sessions

## � **Feature Demonstrations**

See MultiClicker in action with these demonstration videos showcasing key features:

### ⚡ **Click Automation**
[📹 View Simple and No-Delay Click Demo](https://github.com/user-attachments/assets/df8ac0ff-0733-4755-9762-071d03bf3473)

*Demonstration of single clicks with delay vs instant no-delay clicks across multiple Dofus windows*

### 👆 **Double Click Actions**  
[📹 View Double Click Demo](https://github.com/user-attachments/assets/817a2b09-24fa-429c-bc42-1cf5a3b13548)

*Shows the double-click functionality working simultaneously across all managed windows*

### 🌍 **Travel & Group Commands**
[📹 View Travel Feature Demo](https://github.com/user-attachments/assets/5b5246c6-ee2f-4df6-9e23-48777aefe462)

*Demonstrates sending travel commands and text to multiple characters simultaneously*

### 💰 **HDV (Market) Operations**
[📹 View HDV Automation Demo](https://github.com/user-attachments/assets/08eb30da-e5a1-4931-8e34-fa4c352edc77)

*Complete workflow demonstration of HDV slot filling and market automation features*

### 📋 **Paste on All Windows**
[📹 View Paste All Demo](https://github.com/user-attachments/assets/dbf8d646-9106-4ff6-a71a-fd41ec9aa783)

*Shows the clipboard paste functionality distributing content to all managed Dofus windows*

## �🎯 **Comprehensive Keybind System**

The application provides a complete set of keyboard shortcuts and mouse bindings for efficient multi-account management:

### 🎪 **Default Character Navigation**
- **🔄 SELECT_NEXT** (`F1` key): Navigate to the next character in sequence
- **⏮️ SELECT_PREVIOUS** (`F2` key): Navigate to the previous character in sequence

### 🎯 **Click Actions**
- **👆 SIMPLE_CLICK** (`XButton1`): Execute single clicks with configurable delays
- **⚡ DOUBLE_CLICK** (`Middle Mouse Button`): Perform double-click actions
- **🚀 SIMPLE_CLICK_NO_DELAY** (`XButton2`): Execute instant clicks without delay

### 🏠 **Dofus Game Integration**
- **💬 DOFUS_OPEN_DISCUSSION** (`Tab` key): Open chat/discussion windows

### 👥 **Group Management**
- **🤝 GROUP_CHARACTERS** (`F5` key): Send group invitations to all characters
- **🌍 TRAVEL** (`F6` key): send text (or travel commands) to multiple characters

### 💰 **Market (HDV) Operations**
- **🏪 FILL_HDV** (`² + LClick`): Automate HDV slot filling

### 🛠️ **Utility Functions**
- **📋 PASTE_ON_ALL_WINDOWS** (`Ctrl + Alt + V`): Paste clipboard content to all windows
- **⚙️ OPTIONS** (`F12` key): Access application position settings (needed for HDV related features)

## 🏗️ **Application Architecture**

### 🧠 **Core Components**

#### **🎮 MultiClickerForm.cs - Main Application Interface**
The primary application form that manages:
- Dynamic UI generation and layout management
- Real-time panel arrangement and visual feedback
- Window detection and interaction coordination
- Drag-and-drop functionality for panel reordering

#### **🪟 WindowManagementService.cs - Window Operations**
Handles all window-related operations including:
- Automatic discovery and tracking of Dofus windows
- Click, double-click, and key combination simulation with precise targeting
- Window focus management and interaction state tracking
- Advanced window messaging for seamless automation

#### **⚙️ ConfigurationService.cs - Settings Management**
Manages application configuration through:
- High-performance configuration loading and saving
- Settings validation and error handling
- Hot-reloading capabilities for real-time updates
- Complex nested configuration structure management

#### **🎨 PanelManagementService.cs - Visual Management**
Provides visual management features including:
- Dynamic background image management and customization
- Intelligent image caching for optimal performance
- Visual state management and selection indicators
- Responsive layout adjustments and scaling

#### **🔌 HookManagementService.cs - Input Management**
Manages global input capture through:
- Global hotkey capture with high precision
- Low-level keyboard and mouse hook management
- Real-time input processing and event handling
- Conflict-free keybind management system

#### **👁️ OCRService.cs - Text Recognition**
Provides optical character recognition powered by Tesseract:
- Real-time screen text extraction and analysis
- Multi-language support for international users
- High-accuracy character recognition algorithms
- Optimized performance for gaming scenarios

## 🚀 **Getting Started**

### 📋 **System Requirements**
- **Operating System**: Windows 10/11 (64-bit recommended)
- **.NET Framework**: 4.8 or higher
- **Visual C++ Redistributable**: Included in prerequisites folder
- **Game Client**: Dofus (any recent version)
- **Privileges**: Administrator rights recommended for full functionality but not necessary

### 🛠️ **Building from Source**

For developers who want to build the application from source:

1. **Clone the Repository**
   ```bash
   git clone https://github.com/Alexandrebzk/MultiClicker-SLN.git
   cd MultiClicker-SLN
   ```

2. **Open in Visual Studio**
   - Launch Visual Studio 2019 or newer
   - Open `MultiClicker SLN.sln`
   - Ensure all prerequisites are installed (Win forms)

3. **Restore Dependencies**
   - Right-click solution → "Restore NuGet Packages"
   - Wait for automatic dependency installation

4. **Build and Run**
   - Press `Ctrl + B` to build the solution
   - Press `F5` to run with debugging enabled
   - The application will launch automatically

### ⚡ **Quick Start Guide**

1. **Installation**: Download the latest release and extract to your preferred directory
2. **Administrator Access**: Run `MultiClicker.exe` as administrator for full functionality (but not necessary)
3. **Launch Dofus**: Start your Dofus client instances (automatically detected)
4. **Panel Management**: Character panels will appear automatically for each detected window
## 📋 **Configuration Overview**

### 🎯 **Configuration File Structure**
The application uses a structured `config.json` file organized into the following sections:

#### **⚙️ General Settings**
```json
{
  "General": {
    "GameVersion": "3.2.5.5",        // Current Dofus version for compatibility
    "MinimumFollowDelay": 50,        // Minimum delay between actions (milliseconds)
    "MaximumFollowDelay": 80         // Maximum delay for natural variation
  }
}
```

#### **🎨 Panel Customization (generated)**
```json
{
  "Panels": {
    "character-name-1": {
      "Background": "cosmetics\\default.png"  // Character panel background theme
    },
    "character-name-2": {
      "Background": "D:\\...\\red.png"       // Custom background path
    }
    // Additional character themes supported
  }
}
```

#### **🎯 Position Configurations**
Precise positioning data for UI elements including:
- **SELL_CURRENT_MODE**: Current selling mode button location
- **SELL_LOT_1/10/100/1000**: Quantity-based selling button positions
- All positions defined with X, Y, Width, and Height coordinates
- Click Update, get the focus on the desired Dofus window and then right click on the rectangle corners, a rectangle for the designated position will appear

## 🏆 **Technical Specifications**

### 🔥 **Performance Characteristics**
- **Application Startup**: Typically under 2 seconds on modern hardware
- **Hotkey Response Time**: Less than 10ms for registered key combinations
- **Memory Footprint**: Base usage ~30MB
- **Scalability**: Tested with 8 simultaneous character windows

### 🛡️ **Security and Reliability**
- **Safe Automation**: Implements natural timing variations to respect game policies
- **Input Validation**: Comprehensive validation of all configuration parameters
- **Error Handling**: Robust error recovery and user notification systems (available in the trace.log file)
- **Resource Management**: Automatic cleanup and memory optimization

## 📚 **Project Dependencies**

The application is built using the following libraries:

- **🔧 Newtonsoft.Json 13.0.3**: High-performance JSON serialization and deserialization
- **🧠 System.CodeDom 8.0.0**: Code generation and compilation support  
- **💻 System.Management 8.0.0**: System monitoring and management capabilities
- **👁️ Tesseract 5.2.0**: Advanced optical character recognition (OCR) engine
- **🏗️ .NET Framework 4.8**: Stable foundation for Windows desktop applications

## 🎮 **Game Compatibility**

### ✅ **Supported Versions**
- **Dofus 3.x Series**: Supported (Input the game version in the config section)
- **Dofus Retro**: Did not test but should work

### 🌟 **Game-Specific Features**
- **Market (HDV) Operations**: Complete automation workflow for buying and selling
- **Group Coordination**: Advanced party management and invitation systems
- **Travel Synchronization**: Coordinated movement across multiple characters
- **Instant Quest Validation**: Thanks to the simultaneous clicking mechanism, you can validate quests all at once !

## 🔮 **Development Roadmap**

### 🚀 **Planned Enhancements**
- **Enhanced Automation**: Advanced scripting capabilities and behavior patterns
- **Macro System**: Recording and playback of complex action sequences

### 🌟 **Community-Requested Features**

## 💡 **Contributing**

We welcome contributions from the development community. Here are ways to get involved:

### 🛠️ **Contribution Types**
- **Bug Reports**: Submit detailed issue reports with reproduction steps
- **Feature Requests**: Propose new functionality with clear use cases
- **Code Contributions**: Submit pull requests with improvements or new features
- **Documentation**: Enhance guides, tutorials, and code documentation
- **Localization**: Provide translations for international users

### 📞 **Communication Channels**
- **GitHub Issues**: Primary platform for bug reports and feature requests
- **Pull Requests**: Code contributions and improvements

## 📄 **License**

This project is distributed under the MIT License, promoting open-source collaboration and accessibility.

### ⚖️ **Usage Guidelines**
- This tool is designed for legitimate multi-account management
- Users are responsible for compliance with game Terms of Service
- The application should be used in accordance with fair play principles
- This project is not affiliated with or endorsed by Ankama Games

## 🎯 **Get Started Today**

MultiClicker provides tools for Dofus multi-account management. Whether you're managing a small team of characters or operating a complex multi-account setup, this application delivers the performance, reliability, and features you need for efficient gameplay.

**Download the latest release and experience enhanced multi-account management for Dofus.**
## 📋 **Configuration Deep Dive - Master Your Setup!**

Your `config.json` must be up to date:

#### **⚙️ General Settings**
```json
{
  "General": {
    "GameVersion": "3.2.5.5",        // Track your Dofus version for compatibility
    ...
  }
}
```

## 📄 **License & Legal - Important Stuff!**

This project is lovingly crafted and shared under the MIT License - because great tools should be accessible to everyone! 

## 🎉 **Ready to Transform Your Dofus Experience?**

Download MultiClicker today and join thousands (joking, just me) of players who have revolutionized their multi-account gameplay! Whether you're a casual player managing a few characters or a hardcore multi-accounter running a small army, MultiClicker has the tools, features, and performance you need to dominate the World of Twelve! 🌟

**Happy Gaming, and May Your Adventures Be Epic!** 🗡️⚔️🛡️