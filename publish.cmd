@echo off
git add .
git commit -m "Release build" 2>nul
git push origin master
echo ✅ Pushed to GitHub. Check Actions tab for automated release.
