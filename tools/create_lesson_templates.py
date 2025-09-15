#!/usr/bin/env python3
"""
Lesson Template Generator
Creates README templates for all course lessons
"""

import os
from pathlib import Path

# Course structure based on existing lesson names
LESSONS = [
    {"id": "01", "name": "intro-bienvenida", "type": "Introduction", "focus": "Course Welcome and Overview"},
    {"id": "02", "name": "theory-00-presentacion", "type": "Theory", "focus": "Course Presentation"},
    {"id": "03", "name": "theory-01-introduccion-al-dinero-y-mercados", "type": "Theory", "focus": "Introduction to Money and Financial Markets"},
    {"id": "04", "name": "theory-02-introduccion", "type": "Theory", "focus": "General Introduction to Trading"},
    {"id": "05", "name": "theory-03-historia-trading-algoritmico", "type": "Theory", "focus": "History of Algorithmic Trading"},
    {"id": "06", "name": "theory-04-aspectos-psicologicos-perfil-trader", "type": "Theory", "focus": "Psychological Aspects: The Trader's Profile"},
    {"id": "07", "name": "theory-05-desarrollo-de-nuestro-sistema", "type": "Theory", "focus": "Development of Our Trading System"},
    {"id": "08", "name": "theory-06-exposicion-gestion-monetaria", "type": "Theory", "focus": "Exposure and Money Management"},
    {"id": "09", "name": "theory-07-portfolio-de-estrategias", "type": "Theory", "focus": "Strategy Portfolio Management"},
    {"id": "10", "name": "theory-08-cierre-parte-teorica", "type": "Theory", "focus": "Closing of Theoretical Part"},
    {"id": "11", "name": "practice-01", "type": "Practical Session", "focus": "First Trading Implementation"},
    {"id": "12", "name": "practice-02", "type": "Practical Session", "focus": "Advanced Strategy Development"},
    {"id": "13", "name": "practice-03", "type": "Practical Session", "focus": "Risk Management Implementation"},
    {"id": "14", "name": "practice-04", "type": "Practical Session", "focus": "Portfolio Optimization"},
    {"id": "15", "name": "practice-05", "type": "Practical Session", "focus": "Backtesting and Validation"},
    {"id": "16", "name": "practice-06", "type": "Practical Session", "focus": "Live Trading Simulation"},
    {"id": "17", "name": "practice-07", "type": "Practical Session", "focus": "Advanced Analytics"},
    {"id": "18", "name": "practice-08", "type": "Practical Session", "focus": "Multi-Asset Strategies"},
    {"id": "19", "name": "practice-09", "type": "Practical Session", "focus": "Automated Execution"},
    {"id": "20", "name": "practice-10", "type": "Practical Session", "focus": "Performance Analysis"},
    {"id": "21", "name": "practice-11", "type": "Practical Session", "focus": "Strategy Refinement"},
    {"id": "22", "name": "practice-12", "type": "Practical Session", "focus": "Market Regime Analysis"},
    {"id": "23", "name": "practice-13", "type": "Practical Session", "focus": "Alternative Data Integration"},
    {"id": "24", "name": "practice-14", "type": "Practical Session", "focus": "Machine Learning Applications"},
    {"id": "25", "name": "practice-15", "type": "Practical Session", "focus": "Real-time Trading Systems"},
    {"id": "26", "name": "practice-16", "type": "Practical Session", "focus": "Advanced Risk Metrics"},
    {"id": "27", "name": "practice-17", "type": "Practical Session", "focus": "Final Strategy Implementation"},
    {"id": "28", "name": "bonus-vps", "type": "Bonus Content", "focus": "VPS Setup and Deployment"}
]

def create_readme_template(lesson_id, lesson_name, lesson_type, lesson_focus):
    """Create README template for a lesson"""
    template = f"""# {lesson_name.replace('-', ' ').title()} - Algorithmic Trading Course

## 📊 Lesson Overview
**Status**: 🔄 Processing  
**Duration**: ~ 3 hours (original) → Processed content  
**Type**: {lesson_type}  
**Focus**: {lesson_focus}

## 📋 Contents Generated

### 📝 Documentation
- [ ] `transcript.clean.md` - Clean, timestamped transcription
- [ ] `golden-points.md` - Key insights and concepts
- [ ] `academic-research.md` - 🆕 Academic papers and references
- [ ] `scientific-validation.md` - 🆕 Empirical studies and validation
- [ ] `analysis.md` - Technical analysis and strategic insights

### 💻 Code Assets
- [ ] **TradeStation**: EasyLanguage implementations
- [ ] **TradingView**: Pine Script equivalents  
- [ ] **Python**: Algorithmic trading implementations

### 🎯 Interactive Content
- [ ] `practice-exercises.ipynb` - Hands-on exercises
- [ ] `strategy-backtest.ipynb` - Strategy testing notebook

### 📸 Media Assets
- [ ] **Screenshots**: Key moments from instructor demonstration
- [ ] **Diagrams**: Conceptual illustrations and strategy flows

## 🔍 Key Concepts Discovered
*[Will be populated automatically during processing]*

## 📚 Academic Background
*[Will be populated by Research Agent]*

## ⚡ Quick Access
- **Original Video**: [Link will be added]
- **Timestamp Index**: [Generated during processing]
- **Code Repository**: `./code/`
- **Practice Materials**: `./practice-exercises.ipynb`

---
*🤖 This lesson structure was generated by the Algorithmic Trading Course Processor*
"""
    return template

def main():
    """Generate all lesson README files"""
    base_path = Path(__file__).parent.parent / "lessons"
    
    for lesson in LESSONS:
        lesson_dir = base_path / f"{lesson['id']}-{lesson['name']}"
        lesson_dir.mkdir(parents=True, exist_ok=True)
        
        readme_path = lesson_dir / "README.md"
        if not readme_path.exists():  # Don't overwrite existing files
            template = create_readme_template(
                lesson['id'], 
                lesson['name'], 
                lesson['type'], 
                lesson['focus']
            )
            
            with open(readme_path, 'w', encoding='utf-8') as f:
                f.write(template)
            
            print(f"✅ Created README for lesson {lesson['id']}-{lesson['name']}")
        else:
            print(f"⚠️  README already exists for lesson {lesson['id']}-{lesson['name']}")

if __name__ == "__main__":
    main()