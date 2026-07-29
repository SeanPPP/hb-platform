require "json"

package = JSON.parse(File.read(File.join(__dir__, "..", "package.json")))

Pod::Spec.new do |s|
  s.name = "HBExternalDisplay"
  s.module_name = "HBExternalDisplay"
  s.version = package["version"]
  s.summary = package["description"]
  s.description = package["description"]
  s.license = "UNLICENSED"
  s.author = "HB POS"
  s.homepage = "https://hotbargain.vip"
  s.platforms = { :ios => "17.0" }
  s.source = { :git => "https://example.invalid/hb-external-display.git" }
  s.static_framework = true
  s.dependency "Expo"
  s.dependency "ExpoModulesCore"
  s.frameworks = "AVFoundation", "UIKit"
  s.source_files = "**/*.{h,m,mm,swift}"
  s.swift_version = "5.9"
end
