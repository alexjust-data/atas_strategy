#!/usr/bin/env python3
"""
MCP Content Analysis Server
Analyzes trading course transcripts to extract key concepts, golden points, and educational content
"""

import asyncio
import json
import logging
import re
import unicodedata
from pathlib import Path
from typing import Dict, Any, List, Optional
from dataclasses import dataclass
from difflib import SequenceMatcher

from fastmcp import FastMCP

# ──────────────────────────────────────────────────────────────────────────────
# Logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s | %(levelname)s | %(message)s"
)
log = logging.getLogger("mcp-content-analysis")

# MCP server
mcp = FastMCP("Content Analysis Server")

# ──────────────────────────────────────────────────────────────────────────────
# Utility Functions

def _ts(sec: float) -> str:
    """Format timestamp as hh:mm:ss"""
    s = int(max(0, sec))
    return f"{s//3600:02d}:{(s%3600)//60:02d}:{s%60:02d}"

def _norm(s: str) -> str:
    """Normalize text: lowercase + remove accents"""
    return ''.join(c for c in unicodedata.normalize('NFD', s.lower()) 
                  if unicodedata.category(c) != 'Mn')

def _has_term(text: str, term: str) -> bool:
    """Check if term exists in text with word boundaries"""
    pattern = rf'(?<!\w){re.escape(_norm(term))}(?!\w)'
    return re.search(pattern, _norm(text)) is not None

def _is_duplicate(text_a: str, text_b: str, threshold: float = 0.9) -> bool:
    """Check if two texts are similar enough to be considered duplicates"""
    return SequenceMatcher(None, text_a, text_b).ratio() >= threshold

# ──────────────────────────────────────────────────────────────────────────────
# Data Structures

@dataclass
class GoldenPoint:
    text: str
    timestamp: Optional[str]
    importance: int  # 1-5 scale
    category: str    # concept, strategy, warning, insight, etc.

@dataclass
class TradingConcept:
    name: str
    description: str
    mentioned_at: List[str]  # timestamps
    category: str  # indicator, strategy, risk_management, platform, etc.
    complexity: int  # 1-5 scale

@dataclass
class CodeReference:
    platform: str  # tradestation, tradingview, python, etc.
    description: str
    mentioned_at: str
    code_snippet: Optional[str] = None

@dataclass
class ContentAnalysis:
    golden_points: List[GoldenPoint]
    trading_concepts: List[TradingConcept]
    code_references: List[CodeReference]
    educational_structure: Dict[str, Any]
    academic_research_triggers: List[str]
    summary: Dict[str, Any]

# ──────────────────────────────────────────────────────────────────────────────
# Enhanced Trading Knowledge Base with Context Awareness

# Comprehensive trading dictionary with synonyms, translations, and context patterns
TRADING_KNOWLEDGE_BASE = {
    # Technical Indicators
    'rsi': {
        'variants': ['rsi', 'relative strength index', 'índice de fuerza relativa', 'oscillator rsi'],
        'category': 'indicator',
        'complexity': 3,
        'context_patterns': [r'rsi\s+(?:de|en|above|below|por encima|por debajo)', r'índice.*fuerza.*relativa'],
        'description': 'Oscillador de momentum que mide velocidad y cambios de movimientos de precios'
    },
    'macd': {
        'variants': ['macd', 'moving average convergence divergence', 'convergencia divergencia medias móviles'],
        'category': 'indicator', 'complexity': 4,
        'context_patterns': [r'macd\s+(?:line|histogram|signal)', r'convergencia.*divergencia'],
        'description': 'Indicador de momentum que sigue tendencias mediante relación entre dos medias móviles'
    },
    'bollinger_bands': {
        'variants': ['bollinger', 'bollinger bands', 'bandas de bollinger', 'bandas bollinger'],
        'category': 'indicator', 'complexity': 3,
        'context_patterns': [r'bandas?\s+(?:de\s+)?bollinger', r'bollinger\s+bands?'],
        'description': 'Indicador de volatilidad con bandas superior e inferior alrededor de media móvil'
    },
    'moving_average': {
        'variants': ['ma', 'sma', 'ema', 'media móvil', 'moving average', 'media movil', 'promedio móvil'],
        'category': 'indicator', 'complexity': 2,
        'context_patterns': [r'media\s+m[óo]vil', r'moving\s+average', r'\bma\b', r'\bsma\b', r'\bema\b'],
        'description': 'Indicador de tendencia que suaviza datos de precios creando precio promedio actualizado constantemente'
    },
    
    # Trading Strategies
    'scalping': {
        'variants': ['scalping', 'scalp', 'escalpeo'],
        'category': 'strategy', 'complexity': 5,
        'context_patterns': [r'scalp(?:ing)?', r'escalpeo', r'operaciones\s+muy\s+cortas'],
        'description': 'Estrategia de trading de muy corto plazo con múltiples operaciones pequeñas'
    },
    'day_trading': {
        'variants': ['day trading', 'daytrading', 'intradía', 'intradiario', 'trading diario'],
        'category': 'strategy', 'complexity': 4,
        'context_patterns': [r'day\s+trading', r'intrad[íi]a', r'mismo\s+d[íi]a'],
        'description': 'Compra y venta de valores dentro del mismo día de negociación'
    },
    'swing_trading': {
        'variants': ['swing trading', 'swing', 'trading de oscilación'],
        'category': 'strategy', 'complexity': 3,
        'context_patterns': [r'swing\s+trading', r'oscilaci[óo]n', r'varios\s+d[íi]as'],
        'description': 'Estrategia que mantiene posiciones de varios días a semanas para capturar movimientos de precio'
    },
    
    # Risk Management
    'stop_loss': {
        'variants': ['stop loss', 'stop-loss', 'parada de pérdidas', 'corte de pérdidas', 'sl'],
        'category': 'risk_management', 'complexity': 3,
        'context_patterns': [r'stop\s*loss', r'parada.*p[ée]rdidas', r'corte.*p[ée]rdidas'],
        'description': 'Orden para cerrar posición cuando alcanza nivel de pérdida predeterminado'
    },
    'take_profit': {
        'variants': ['take profit', 'take-profit', 'toma de ganancias', 'beneficios', 'tp'],
        'category': 'risk_management', 'complexity': 3,
        'context_patterns': [r'take\s*profit', r'toma.*ganancias', r'realizar\s+beneficios'],
        'description': 'Orden para cerrar posición cuando alcanza nivel de ganancia objetivo'
    },
    'position_sizing': {
        'variants': ['position sizing', 'tamaño de posición', 'gestión de capital', 'sizing'],
        'category': 'risk_management', 'complexity': 4,
        'context_patterns': [r'position\s+sizing', r'tama[ñn]o.*posici[óo]n', r'gesti[óo]n.*capital'],
        'description': 'Determinación del tamaño apropiado de inversión para cada operación'
    },
    
    # Platforms and Tools
    'tradestation': {
        'variants': ['tradestation', 'trade station', 'ts'],
        'category': 'platform', 'complexity': 2,
        'context_patterns': [r'trade\s*station', r'\bts\b.*plataforma'],
        'description': 'Plataforma de trading profesional con capacidades de análisis técnico y automatización'
    },
    'tradingview': {
        'variants': ['tradingview', 'trading view', 'tv'],
        'category': 'platform', 'complexity': 2,
        'context_patterns': [r'trading\s*view', r'\btv\b.*chart'],
        'description': 'Plataforma web de gráficos y análisis técnico con comunidad de traders'
    },
    'easylanguage': {
        'variants': ['easylanguage', 'easy language', 'el'],
        'category': 'platform', 'complexity': 4,
        'context_patterns': [r'easy\s*language', r'\bel\b.*c[óo]digo'],
        'description': 'Lenguaje de programación específico para estrategias de trading en TradeStation'
    },
    
    # Academic Concepts
    'backtesting': {
        'variants': ['backtesting', 'backtest', 'prueba histórica', 'test histórico'],
        'category': 'academic', 'complexity': 4,
        'context_patterns': [r'back\s*test(?:ing)?', r'prueba.*hist[óo]rica', r'datos.*hist[óo]ricos'],
        'description': 'Evaluación de estrategia usando datos históricos para medir rendimiento'
    },
    'overfitting': {
        'variants': ['overfitting', 'over-fitting', 'sobreajuste', 'sobre-ajuste'],
        'category': 'academic', 'complexity': 5,
        'context_patterns': [r'over\s*fitting', r'sobre\s*ajuste', r'optimizaci[óo]n.*excesiva'],
        'description': 'Problema estadístico donde modelo se ajusta demasiado a datos específicos perdiendo capacidad predictiva'
    },
    'walk_forward': {
        'variants': ['walk forward', 'walk-forward', 'validación progresiva', 'análisis progresivo'],
        'category': 'academic', 'complexity': 5,
        'context_patterns': [r'walk\s*forward', r'validaci[óo]n.*progresiva', r'an[áa]lisis.*progresivo'],
        'description': 'Método de validación que optimiza parámetros en ventana móvil temporal'
    },
    'mean_reversion': {
        'variants': ['mean reversion', 'reversión a la media', 'reversión', 'MR', 'regression to the mean'],
        'category': 'strategy', 'complexity': 4,
        'context_patterns': [r'reversi[óo]n\s+a\s+la\s+media', r'mean\s+reversion', r'regression.*mean'],
        'description': 'Estrategia que asume que el precio tiende a volver a su media histórica'
    },
    'momentum_strategy': {
        'variants': ['momentum', 'estrategia de momentum', 'momentum trading', 'fuerza relativa'],
        'category': 'strategy', 'complexity': 4,
        'context_patterns': [r'\bmomentum\b', r'fuerza\s+relativa', r'momento.*precio'],
        'description': 'Estrategia que sigue la inercia de los movimientos de precio en la dirección dominante'
    },
    'portfolio_theory': {
        'variants': ['portfolio theory', 'teoría de portafolios', 'teoría de carteras', 'modern portfolio theory', 'MPT'],
        'category': 'academic', 'complexity': 5,
        'context_patterns': [r'portfolio\s+theory', r'teor[íı]a.*portafoli', r'teor[íı]a.*cartera', r'\bmpt\b'],
        'description': 'Teoría que optimiza el balance riesgo-retorno en una cartera de inversiones'
    },
    'diversification': {
        'variants': ['diversification', 'diversificación', 'diversificar', 'spread risk'],
        'category': 'risk_management', 'complexity': 3,
        'context_patterns': [r'diversificaci[óo]n', r'diversificar', r'spread\s+risk', r'no.*todos.*huevos'],
        'description': 'Estrategia de riesgo que distribuye inversiones en diferentes activos para reducir exposición'
    }
}

# Flattened variants for term-density checks
ALL_TRADING_VARIANTS = set()
for data in TRADING_KNOWLEDGE_BASE.values():
    ALL_TRADING_VARIANTS.update(data["variants"])

# Legacy sets for compatibility
TRADING_INDICATORS = {concept for concept, data in TRADING_KNOWLEDGE_BASE.items() 
                     if data['category'] == 'indicator'}
TRADING_STRATEGIES = {concept for concept, data in TRADING_KNOWLEDGE_BASE.items() 
                     if data['category'] == 'strategy'}
RISK_MANAGEMENT = {concept for concept, data in TRADING_KNOWLEDGE_BASE.items() 
                  if data['category'] == 'risk_management'}
PLATFORMS_TOOLS = {concept for concept, data in TRADING_KNOWLEDGE_BASE.items() 
                  if data['category'] == 'platform'}
ACADEMIC_CONCEPTS = {concept for concept, data in TRADING_KNOWLEDGE_BASE.items() 
                    if data['category'] == 'academic'}

# ──────────────────────────────────────────────────────────────────────────────
# Core Analysis Functions

def _extract_smart_context(text: str, i: int, j: int, max_chars: int = 220) -> str:
    """Extract intelligent context around a pattern match - prevents index misalignment"""
    # i, j are start/end in the ORIGINAL TEXT
    left_delims  = r"[\.!\?\n¿¡]"   # includes line breaks and Spanish signs
    right_delims = r"[\.!\?\n]"
    
    # Find delimiter to the left
    L = 0
    matches = list(re.finditer(left_delims, text[:i]))
    if matches:
        L = matches[-1].end()
    
    # Find delimiter to the right
    R = len(text)
    match = re.search(right_delims, text[j:])
    if match:
        R = j + match.end()
    
    # Get the span
    span = text[L:R].strip()
    
    # Limit by maximum length if needed
    if len(span) > max_chars:
        k = max(i - max_chars//2, L)
        t = min(j + max_chars//2, R)
        span = text[k:t].strip()
    
    return span

def _calculate_importance_score(context: str, base_score: int, pattern_type: str, full_text: str) -> int:
    """Calculate enhanced importance score with multiple criteria"""
    score = base_score
    
    # Length factor (longer explanations often more valuable)
    if len(context) > 100:
        score += 1
    
    # Trading terminology density using ALL variants
    trading_terms = len([term for term in ALL_TRADING_VARIANTS
                        if _has_term(context, term)])
    if trading_terms >= 2:
        score += 1
    
    # Emphasis indicators
    emphasis_patterns = [r'\b(muy|realmente|especialmente|particularmente)\b',
                        r'[A-Z]{3,}',  # CAPS emphasis
                        r'!!+',  # Multiple exclamations
                        r'\*.*\*']  # Asterisk emphasis
    
    emphasis_count = sum(1 for pattern in emphasis_patterns 
                        if re.search(pattern, context, re.IGNORECASE))
    score += min(emphasis_count, 2)
    
    # Negation penalty
    if re.search(r'\b(no\s+siempre|no\s+es|pero\s+no|sin embargo|aunque)\b', context, re.I):
        score = max(1, score - 1)
    
    # Specific question/answer bonus
    if re.search(r'\b(por qué|cómo|qué pasa si|qué significa)\b', context, re.I):
        score += 1
    
    # Context within full segment (start/end often less important)
    segment_position = full_text.find(context) / len(full_text)
    if 0.2 <= segment_position <= 0.8:  # Middle of segment
        score += 1
    
    return max(1, min(5, score))

def _is_semantic_duplicate(text1: str, text2: str, threshold: float = 0.85) -> bool:
    """Enhanced duplicate detection with semantic similarity"""
    # Exact similarity
    if SequenceMatcher(None, text1, text2).ratio() >= threshold:
        return True
    
    # Normalized similarity (remove accents, lowercase)
    norm1, norm2 = _norm(text1), _norm(text2)
    if SequenceMatcher(None, norm1, norm2).ratio() >= threshold:
        return True
    
    # Key phrase similarity (extract important words)
    important_words1 = set(_extract_key_words(text1))
    important_words2 = set(_extract_key_words(text2))
    
    if important_words1 and important_words2:
        word_overlap = len(important_words1 & important_words2) / len(important_words1 | important_words2)
        if word_overlap >= 0.7:
            return True
    
    return False

def _extract_key_words(text: str) -> List[str]:
    """Extract key words (nouns, trading terms, important adjectives)"""
    # Remove common stop words
    stop_words = {'el', 'la', 'de', 'que', 'y', 'es', 'en', 'un', 'se', 'no', 'te', 'lo', 'le', 'da', 'su', 'por', 'son', 'con', 'para', 'al', 'del', 'los', 'las', 'una', 'uno', 'sobre', 'todo', 'pero', 'sus', 'muy', 'sin', 'puede', 'ser', 'está', 'tiene', 'han', 'hay', 'o', 'si', 'ya', 'vez', 'bien', 'donde', 'cuando', 'como', 'tanto', 'entre', 'hasta', 'antes', 'después', 'ahora', 'aquí', 'allí'}
    
    words = re.findall(r'\b\w{3,}\b', text.lower())
    return [word for word in words if word not in stop_words and len(word) >= 3]

def _filter_and_rank_golden_points(golden_points: List[GoldenPoint]) -> List[GoldenPoint]:
    """Advanced filtering and ranking of golden points"""
    if not golden_points:
        return []
    
    # Group by category to ensure diversity
    category_groups = {}
    for gp in golden_points:
        if gp.category not in category_groups:
            category_groups[gp.category] = []
        category_groups[gp.category].append(gp)
    
    # Sort each category by importance
    for category in category_groups:
        category_groups[category].sort(key=lambda x: x.importance, reverse=True)
    
    # Select diverse set (max 3 per category, prioritize high importance)
    result = []
    max_per_category = 3
    
    # First pass: take top item from each category
    for category, items in category_groups.items():
        if items:
            result.append(items[0])
    
    # Second pass: fill remaining slots with high-importance items
    remaining_slots = 15 - len(result)
    all_remaining = []
    for category, items in category_groups.items():
        all_remaining.extend(items[1:max_per_category])  # Skip first (already added)
    
    # Sort by importance and add remaining
    all_remaining.sort(key=lambda x: x.importance, reverse=True)
    result.extend(all_remaining[:remaining_slots])
    
    # Final sort by importance
    result.sort(key=lambda x: x.importance, reverse=True)
    return result

def extract_golden_points(transcript: str, segments: List[Dict]) -> List[GoldenPoint]:
    """Extract key insights with advanced multi-criteria scoring"""
    golden_points = []
    
    # Enhanced golden point patterns with weighted importance
    CRITICAL_PATTERNS = [
        (r'(?:muy\s+)?(?:importante|clave|fundamental|esencial|crucial)(?:\s+es|\s+que|:)', 5, 'critical_insight'),
        (r'(?:recordad|recuerden|tengan en cuenta|hay que tener en cuenta)', 5, 'reminder'),
        (r'(?:cuidado|atención|ojo)\s+(?:con|que)', 5, 'warning'),
        (r'(?:error|fallo|problema)\s+(?:común|típico|habitual)', 5, 'common_mistake'),
        (r'(?:regla|principio)\s+(?:básico|fundamental)', 5, 'rule')
    ]
    
    HIGH_VALUE_PATTERNS = [
        (r'(?:siempre|nunca)\s+(?:hag|deb|ten)', 4, 'best_practice'),
        (r'(?:esto es|aquí está|la clave está)', 4, 'key_concept'),
        (r'(?:mi consejo|mi recomendación|recomiendo)', 4, 'recommendation'),
        (r'(?:secreto|truco|tip)(?:\s+es|\s+que|:)', 4, 'tip'),
        (r'(?:ventaja|desventaja)(?:\s+es|\s+de)', 4, 'pros_cons')
    ]
    
    MEDIUM_VALUE_PATTERNS = [
        (r'(?:diferencia|distinción)\s+(?:entre|de)', 3, 'comparison'),
        (r'(?:ejemplo|caso)(?:\s+de|\s+práctico)', 3, 'example'),
        (r'(?:experiencia|práctica)\s+(?:me|nos)\s+(?:dice|enseña)', 3, 'experience'),
        (r'(?:resultado|conclusión)(?:\s+es|\s+que)', 3, 'conclusion')
    ]
    
    # Trading-specific high-value patterns
    TRADING_PATTERNS = [
        (r'(?:señal|entrada|salida)\s+(?:de|para)', 4, 'signal'),
        (r'(?:gestión|management)\s+(?:del\s+)?(?:riesgo|capital)', 5, 'risk_management'),
        (r'(?:backtesting|optimización)(?:\s+es|\s+de|\s+se)', 4, 'methodology'),
        (r'(?:mercado|precio)\s+(?:hace|tiende|suele)', 4, 'market_behavior'),
        (r'(?:timeframe|marco\s+temporal)(?:\s+de|\s+para)', 3, 'timeframe')
    ]
    
    all_patterns = CRITICAL_PATTERNS + HIGH_VALUE_PATTERNS + MEDIUM_VALUE_PATTERNS + TRADING_PATTERNS
    
    for seg in segments:
        text = seg.get('text', '').strip()
        start_time = seg.get('start', 0)
        
        # Look for golden point patterns
        for pattern, base_importance, pattern_type in all_patterns:
            matches = re.finditer(pattern, text, re.IGNORECASE)
            for match in matches:
                # Extract surrounding context with intelligent boundaries
                context = _extract_smart_context(text, match.start(), match.end())
                
                if len(context) > 25:  # Minimum meaningful length
                    # Multi-criteria scoring
                    final_score = _calculate_importance_score(
                        context, base_importance, pattern_type, text
                    )
                    
                    # Check for duplicates with semantic similarity
                    is_duplicate = any(
                        _is_semantic_duplicate(context, gp.text, threshold=0.85) 
                        for gp in golden_points
                    )
                    
                    if not is_duplicate and final_score >= 3:
                        golden_points.append(GoldenPoint(
                            text=context,
                            timestamp=_ts(start_time),
                            importance=min(5, final_score),  # Cap at 5
                            category=pattern_type
                        ))
    
    # Advanced filtering and ranking
    golden_points = _filter_and_rank_golden_points(golden_points)
    return golden_points[:15]  # Top 15 with better quality

def _find_concept_in_text(text: str, concept_key: str, concept_data: Dict) -> Optional[Dict]:
    """Advanced concept detection with context patterns and variants"""
    variants = concept_data['variants']
    context_patterns = concept_data.get('context_patterns', [])
    
    # Check variants with word boundaries
    for variant in variants:
        if _has_term(text, variant):
            # If context patterns exist, verify one matches
            if context_patterns:
                context_found = any(re.search(pattern, text, re.IGNORECASE) for pattern in context_patterns)
                if not context_found:
                    continue  # Skip if no context pattern matches
            
            # Extract surrounding context for better understanding
            pattern = rf'(?i)\b{re.escape(variant)}\b'
            match = re.search(pattern, text)
            if match:
                context = _extract_smart_context(text, match.start(), match.end(), max_chars=220)
                return {
                    'matched_variant': variant,
                    'context': context,
                    'confidence': _calculate_concept_confidence(text, variant, context_patterns)
                }
    
    return None

def _calculate_concept_confidence(text: str, matched_variant: str, context_patterns: List[str]) -> float:
    """Calculate confidence score for concept detection"""
    base_confidence = 0.7
    
    # Boost for exact technical term matches
    if len(matched_variant) > 8 and matched_variant.lower() in text.lower():
        base_confidence += 0.2
    
    # Boost for context pattern matches
    context_matches = sum(1 for pattern in context_patterns 
                         if re.search(pattern, text, re.IGNORECASE))
    base_confidence += context_matches * 0.1
    
    # Reduce for very common words that might be false positives
    common_words = {'el', 'la', 'ma', 'de', 'es', 'en'}
    if matched_variant.lower() in common_words:
        base_confidence -= 0.3
    
    return min(1.0, max(0.1, base_confidence))

def identify_trading_concepts(transcript: str, segments: List[Dict]) -> List[TradingConcept]:
    """Advanced trading concept identification with context awareness"""
    concepts = {}
    
    for segment in segments:
        text = segment.get('text', '')
        start_time = segment.get('start', 0)
        timestamp = _ts(start_time)
        
        # Check each concept in knowledge base
        for concept_key, concept_data in TRADING_KNOWLEDGE_BASE.items():
            match_result = _find_concept_in_text(text, concept_key, concept_data)
            
            if match_result and match_result['confidence'] >= 0.6:
                if concept_key not in concepts:
                    # Create new concept
                    concepts[concept_key] = TradingConcept(
                        name=concept_data['variants'][0],  # Use primary name
                        description=concept_data['description'],
                        mentioned_at=[timestamp],
                        category=concept_data['category'],
                        complexity=concept_data['complexity']
                    )
                else:
                    # Add additional mention if not too close in time
                    last_timestamp = concepts[concept_key].mentioned_at[-1]
                    # Simple time comparison (convert hh:mm:ss to seconds)
                    def time_to_seconds(time_str):
                        h, m, s = map(int, time_str.split(':'))
                        return h * 3600 + m * 60 + s
                    
                    if abs(time_to_seconds(timestamp) - time_to_seconds(last_timestamp)) > 10:
                        concepts[concept_key].mentioned_at.append(timestamp)
    
    # Sort by complexity and frequency
    concept_list = list(concepts.values())
    concept_list.sort(key=lambda x: (x.complexity, len(x.mentioned_at)), reverse=True)
    return concept_list

def find_code_references(transcript: str, segments: List[Dict]) -> List[CodeReference]:
    """Find references to code, platforms, and programming concepts"""
    code_refs = []
    
    code_patterns = [
        (r'(?:tradestation|easylanguage)', 'tradestation'),
        (r'(?:tradingview|pine\s*script)', 'tradingview'),
        (r'(?:python|pandas|numpy)', 'python'),
        (r'(?:metatrader|mql)', 'metatrader'),
        (r'(?:código|programar|script|función|algoritmo)', 'general'),
        (r'(?:backtest|backtesting|prueba)', 'testing'),
        (r'(?:api|websocket|datos|data)', 'data'),
    ]
    
    for segment in segments:
        text = segment.get('text', '')
        start_time = segment.get('start', 0)
        timestamp = _ts(start_time)
        
        for pattern, platform in code_patterns:
            if re.search(pattern, text, re.IGNORECASE):
                # Extract surrounding context (avoid duplicates)
                context = text.strip()
                
                # Check for existing similar references
                is_similar = any(
                    _is_duplicate(context, ref.description, 0.7) and ref.platform == platform
                    for ref in code_refs
                )
                
                if not is_similar:
                    code_refs.append(CodeReference(
                        platform=platform,
                        description=context,
                        mentioned_at=timestamp,
                        code_snippet=None
                    ))
    
    return code_refs

def _determine_teaching_style(factors: Dict[str, float]) -> str:
    """Heurística simple: prioriza interacción y ejemplos"""
    if factors.get("questions_per_min", 0) >= 0.6 and factors.get("examples_per_min", 0) >= 0.6:
        return "highly_interactive"
    if factors.get("examples_per_min", 0) >= 0.8:
        return "hands_on_practical"
    if factors.get("explanations_per_min", 0) >= 1.0:
        return "lecture_style"
    return "mixed"

def _calculate_engagement_score(segments: List[Dict]) -> Dict[str, Any]:
    """Calculate engagement score based on educational patterns"""
    if not segments:
        return {"score": 0, "teaching_style": "unknown", "factors": {}}
    
    total_sec = max((seg.get("end", 0) for seg in segments), default=0) or 1
    
    # Conteos básicos
    q = egs = exps = 0
    for seg in segments:
        t = seg.get("text", "")
        q  += 1 if re.search(r'\?|pregunt|dud|cómo|qué|por qué|cuándo|dónde', t, re.I) else 0
        egs += 1 if re.search(r'ejemplo|por ejemplo|veamos|vamos a ver|imagin|supong', t, re.I) else 0
        exps+= 1 if re.search(r'esto significa|es decir|o sea|en otras palabras|explicar|definir', t, re.I) else 0
    
    factors = {
        "questions_per_min": q/(total_sec/60),
        "examples_per_min":  egs/(total_sec/60),
        "explanations_per_min": exps/(total_sec/60),
        "segments": len(segments)
    }
    
    # Score 0–10
    score = min(10, round(
        3.5*factors["questions_per_min"] + 4.0*factors["examples_per_min"] + 2.5*factors["explanations_per_min"], 2
    ))
    
    return {
        "score": score, 
        "teaching_style": _determine_teaching_style(factors), 
        "factors": factors
    }

def _analyze_lesson_flow(segments: List[Dict]) -> List[Dict[str, Any]]:
    """Analyze lesson flow and segment types"""
    flow = []
    for seg in segments:
        text = seg.get("text","")
        start = seg.get("start", 0.0)
        end = seg.get("end", start)
        
        kind = "content"
        if re.search(r'\?|pregunt|dud', text, re.I): 
            kind = "question"
        elif re.search(r'ejemplo|por ejemplo|veamos|imagin|supong', text, re.I): 
            kind = "example"
        elif re.search(r'esto significa|es decir|o sea|explicar|definir', text, re.I): 
            kind = "explanation"
        
        flow.append({
            "timestamp": _ts(start), 
            "type": kind, 
            "duration": max(0.0, end-start)
        })
    
    return flow

def analyze_educational_structure(transcript: str, segments: List[Dict]) -> Dict[str, Any]:
    """Analyze the educational structure of the content"""
    structure = {
        'questions_asked': [],
        'examples_provided': [],
        'explanations': [],
        'duration_analysis': {},
        'engagement_indicators': [],
        'lesson_flow': []
    }
    
    # Enhanced patterns with types for variety calculation
    question_patterns = [
        (r'\?', 'direct_question'),
        (r'(?:qué\s+(?:es|significa|pasa|opináis))', 'what_question'),
        (r'(?:cómo\s+(?:se|podemos|haríais))', 'how_question'),
        (r'(?:por\s+qué|porqué)', 'why_question'),
        (r'(?:cuándo|cuánto|dónde)', 'when_where_question'),
        (r'(?:pregunt|dud|consulta)', 'meta_question'),
    ]
    
    example_patterns = [
        (r'(?:ejemplo|por ejemplo|veamos|vamos a ver)', 'example_demo'),
        (r'(?:imagin|supong|pongamos que)', 'hypothetical'),
        (r'(?:caso|situación)\s+(?:práctico|real)', 'real_case'),
        (r'(?:demostración|demo)', 'demonstration'),
    ]
    
    explanation_patterns = [
        (r'(?:esto significa|es decir|o sea|en otras palabras)', 'clarification'),
        (r'(?:explicar|definir|concepto)', 'definition'),
        (r'(?:entender|comprender)', 'understanding'),
        (r'(?:básicamente|resumiendo)', 'summary'),
    ]
    
    for segment in segments:
        text = segment.get('text', '').strip()
        start_time = segment.get('start', 0)
        timestamp = _ts(start_time)
        
        # Analyze segment content
        segment_type = 'content'
        
        # Check for questions
        for pattern, q_type in question_patterns:
            if re.search(pattern, text, re.IGNORECASE):
                structure['questions_asked'].append({
                    'text': text,
                    'timestamp': timestamp,
                    'type': q_type
                })
                segment_type = 'question'
                break
        
        # Check for examples
        for pattern, e_type in example_patterns:
            if re.search(pattern, text, re.IGNORECASE):
                structure['examples_provided'].append({
                    'text': text,
                    'timestamp': timestamp,
                    'type': e_type
                })
                segment_type = 'example'
                break
        
        # Check for explanations
        for pattern, exp_type in explanation_patterns:
            if re.search(pattern, text, re.IGNORECASE):
                structure['explanations'].append({
                    'text': text,
                    'timestamp': timestamp,
                    'type': exp_type
                })
                segment_type = 'explanation'
                break
        
        # Track lesson flow
        structure['lesson_flow'].append({
            'timestamp': timestamp,
            'type': segment_type,
            'duration': segment.get('end', start_time) - start_time
        })
    
    # Enhanced duration analysis
    if segments:
        total_duration = max((seg.get('end', 0) for seg in segments), default=0)
        avg_segment_duration = (
            sum((seg.get('end', 0) - seg.get('start', 0) for seg in segments), 0.0) / len(segments)
        ) if segments else 0
        
        structure['duration_analysis'] = {
            'total_seconds': total_duration,
            'total_formatted': _ts(total_duration),
            'segments_count': len(segments),
            'avg_segment_duration_seconds': avg_segment_duration,
            'avg_segment_duration_formatted': _ts(avg_segment_duration),
            'questions_per_minute': len(structure['questions_asked']) / (total_duration / 60) if total_duration > 0 else 0,
            'examples_per_minute': len(structure['examples_provided']) / (total_duration / 60) if total_duration > 0 else 0
        }
    
    # Calculate comprehensive engagement score
    engagement_analysis = _calculate_engagement_score(segments)
    structure['engagement_score'] = engagement_analysis['score']
    structure['teaching_style'] = engagement_analysis['teaching_style']
    structure['engagement_factors'] = engagement_analysis['factors']
    
    # Analyze lesson flow structure
    structure['lesson_flow'] = _analyze_lesson_flow(segments)
    
    # Learning effectiveness indicators
    structure['learning_indicators'] = {
        'question_variety': len(set(q.get('type', 'unknown') for q in structure['questions_asked'])),
        'example_variety': len(set(e.get('type', 'unknown') for e in structure['examples_provided'])),
        'explanation_depth': len([e for e in structure['explanations'] if len(e['text']) > 100]),
        'interaction_frequency': (len(structure['questions_asked']) + len(structure['examples_provided'])) / len(segments) if segments else 0,
        'educational_completeness': min(10, len(structure.get('lesson_flow', [])) * 2)  # Variety of section types
    }
    
    return structure

def identify_academic_triggers(concepts: List[TradingConcept], golden_points: List[GoldenPoint], transcript: str = "") -> List[str]:
    """Identify concepts that would benefit from academic research"""
    triggers = set()
    
    # High-complexity concepts need academic backing
    for concept in concepts:
        if concept.complexity >= 4 or concept.category == 'academic':
            triggers.add(concept.name)
    
    # Important golden points might reference academic concepts
    academic_keywords = [
        'teoria', 'teoría', 'estudio', 'investigación', 'paper', 
        'académico', 'científico', 'research', 'journal',
        'estadística', 'matemático', 'modelo', 'hipótesis'
    ]
    
    # Search in transcript for academic keywords
    if transcript:
        transcript_lower = transcript.lower()
        for keyword in academic_keywords:
            if keyword in transcript_lower:
                triggers.add(keyword)
    
    # Search golden points for academic references
    for point in golden_points:
        if point.importance >= 4:
            if any(keyword in point.text.lower() for keyword in academic_keywords):
                triggers.add(point.text[:50].strip() + "...")
    
    # Add specific trading academic topics - search in TRANSCRIPT not concept names
    academic_trading_topics = [
        'efficient market hypothesis',
        'random walk theory', 
        'behavioral finance',
        'market microstructure',
        'risk parity',
        'factor investing'
    ]
    
    if transcript:
        transcript_lower = transcript.lower()
        for topic in academic_trading_topics:
            if topic in transcript_lower:
                triggers.add(topic)
    
    return sorted(list(triggers))

def _eq_norm(a: str, b: str) -> bool:
    """Compare two strings with robust normalization (lowercase + no accents)"""
    return _norm(a) == _norm(b)

def _any_variant_in(concept_name: str, keys_or_variants: List[str]) -> bool:
    """Check if concept name matches any variant using robust normalization"""
    return any(_eq_norm(concept_name, variant) for variant in keys_or_variants)

def _concept_matches_category(concept: TradingConcept, category_data: Dict) -> bool:
    """Check if concept matches research category using variants or keywords"""
    # Check if concept name matches any expected concept variants
    concept_variants = TRADING_KNOWLEDGE_BASE.get(concept.name.lower().replace(' ', '_'), {}).get('variants', [concept.name])
    
    # Check against category concepts (using all possible variants)
    for expected_concept in category_data['concepts']:
        expected_variants = TRADING_KNOWLEDGE_BASE.get(expected_concept, {}).get('variants', [expected_concept])
        if _any_variant_in(concept.name, expected_variants) or any(_any_variant_in(variant, expected_variants) for variant in concept_variants):
            return True
    
    # Check against keywords in description
    return any(kw in concept.description.lower() for kw in category_data['keywords'])

def identify_advanced_academic_needs(concepts: List[TradingConcept], golden_points: List[GoldenPoint], transcript: str = "") -> List[Dict[str, Any]]:
    """Advanced identification of concepts requiring academic research with context and priority"""
    research_needs = []
    
    # Academic research categories with specific triggers
    RESEARCH_CATEGORIES = {
        'statistical_validation': {
            'keywords': ['estadística', 'significancia', 'p-value', 'hipótesis', 'test', 'validación', 'bootstrap', 'monte carlo'],
            'concepts': ['backtesting', 'overfitting', 'walk_forward'],
            'priority': 5,
            'description': 'Métodos estadísticos para validar estrategias'
        },
        'market_theory': {
            'keywords': ['eficiencia', 'mercado', 'random walk', 'behavioral', 'anomalía'],
            'concepts': ['mean_reversion', 'momentum_strategy'],  # Updated to match KB keys
            'priority': 4,
            'description': 'Teorías sobre funcionamiento de mercados'
        },
        'risk_models': {
            'keywords': ['var', 'value at risk', 'drawdown', 'volatilidad', 'sharpe', 'sortino'],
            'concepts': ['stop_loss', 'diversification'],  # Updated to match KB keys
            'priority': 4,
            'description': 'Modelos matemáticos de riesgo'
        }
    }
    
    # Analyze high-complexity concepts
    for concept in concepts:
        if concept.complexity >= 4:
            category = 'general'
            priority = concept.complexity
            
            # Categorize more specifically using robust matching
            for cat, data in RESEARCH_CATEGORIES.items():
                if _concept_matches_category(concept, data):
                    category = cat
                    priority = data['priority']
                    break
            
            research_needs.append({
                'topic': concept.name,
                'category': category,
                'priority': priority,
                'description': RESEARCH_CATEGORIES.get(category, {}).get('description', 'Requiere investigación académica'),
                'context': f"Mencionado {len(concept.mentioned_at)} veces",
                'suggested_research': f"Análisis empírico de {concept.name} en mercados financieros"
            })
    
    # Sort by priority and return top items
    research_needs.sort(key=lambda x: x['priority'], reverse=True)
    return research_needs[:8]

# ──────────────────────────────────────────────────────────────────────────────
# Advanced Helper Functions

def generate_shots_todo(analysis: ContentAnalysis, video_path: str, out_dir: str, lesson_name: str = "", clip_duration: int = 15) -> Dict[str, Any]:
    """Generate FFmpeg commands for capturing screenshots AND video clips at golden points timestamps"""
    try:
        base_path = Path(out_dir)
        media_path = base_path / "media"
        shots_path = media_path / "shots"
        clips_path = media_path / "clips" 
        shots_path.mkdir(parents=True, exist_ok=True)
        clips_path.mkdir(parents=True, exist_ok=True)
        
        # Use improved golden points selection
        top_golden_points = _pick_golden_points_for_shots(analysis.golden_points, max_n=10)
        
        if not top_golden_points:
            return {"success": False, "error": "No golden points found for media generation"}
        
        screenshot_commands = []
        video_commands = []
        media_info = []
        
        for i, gp in enumerate(top_golden_points, 1):
            timestamp = _ensure_hhmmss(getattr(gp, "timestamp", None))
            if timestamp == "00:00:00" and hasattr(gp, 'timestamp') and not gp.timestamp:
                continue  # Skip if no valid timestamp
                
            total_seconds = _timestamp_to_seconds(timestamp)
            safe_category = (getattr(gp, "category", "general") or "general").replace(' ', '_').replace('-', '_')
            
            # Screenshot filename and command
            screenshot_file = f"shot_{i:02d}_{safe_category}_{int(total_seconds)}s.png"
            screenshot_path = shots_path / screenshot_file
            screenshot_cmd = f'ffmpeg -ss {timestamp} -i "{video_path}" -vframes 1 -q:v 2 -y "{screenshot_path}"'
            screenshot_commands.append(screenshot_cmd)
            
            # Video clip filename and command (start 5s before, capture clip_duration seconds)
            clip_start = max(0, total_seconds - 5)  # Start 5s before golden point
            clip_start_ts = f"{clip_start//3600:02d}:{(clip_start%3600)//60:02d}:{clip_start%60:02d}"
            clip_file = f"clip_{i:02d}_{safe_category}_{int(total_seconds)}s.mp4"
            clip_path = clips_path / clip_file
            
            # High-quality video clip with fade effects
            video_cmd = (f'ffmpeg -ss {clip_start_ts} -i "{video_path}" -t {clip_duration} '
                        f'-c:v libx264 -crf 18 -preset medium -c:a aac -b:a 192k '
                        f'-vf "fade=in:0:15,fade=out:{clip_duration*30-15}:15" '
                        f'-y "{clip_path}"')
            video_commands.append(video_cmd)
            
            media_info.append({
                'media_number': i,
                'timestamp': timestamp,
                'importance': getattr(gp, 'importance', 1),
                'category': getattr(gp, 'category', 'general'),
                'screenshot_file': screenshot_file,
                'clip_file': clip_file,
                'clip_duration': clip_duration,
                'golden_point_text': (getattr(gp, 'text', '')[:100] + '...' 
                                    if len(getattr(gp, 'text', '')) > 100 
                                    else getattr(gp, 'text', '')),
                'screenshot_command': screenshot_cmd,
                'video_command': video_cmd
            })
        
        # Generate comprehensive media TODO file
        media_todo_content = f"# 📸🎬 Media Generation for {lesson_name}\n\n"
        media_todo_content += f"Generated {len(screenshot_commands)} screenshots and {len(video_commands)} video clips\n\n"
        
        for info in media_info:
            media_todo_content += f"## Media {info['media_number']}: {info['category'].title()} (⭐{info['importance']})\n"
            media_todo_content += f"**Timestamp**: {info['timestamp']}\n"
            media_todo_content += f"**Golden Point**: {info['golden_point_text']}\n\n"
            
            media_todo_content += "### 📸 Screenshot\n"
            media_todo_content += f"```bash\n{info['screenshot_command']}\n```\n"
            media_todo_content += f"**Output**: `media/shots/{info['screenshot_file']}`\n\n"
            
            media_todo_content += "### 🎬 Video Clip\n"
            media_todo_content += f"```bash\n{info['video_command']}\n```\n"
            media_todo_content += f"**Output**: `media/clips/{info['clip_file']}`\n"
            media_todo_content += f"**Duration**: {info['clip_duration']} seconds (starts 5s before golden point)\n\n"
            media_todo_content += "---\n\n"
        
        # Batch script for Windows (screenshots + videos) - executed from scripts/ dir
        batch_content = "@echo off\n"
        batch_content += "cd ..\n"  # Go up to lesson root
        batch_content += f"echo Generating media for {lesson_name}...\n"
        batch_content += f"echo Creating {len(screenshot_commands)} screenshots and {len(video_commands)} video clips\n\n"
        
        batch_content += "echo Generating screenshots...\n"
        for cmd in screenshot_commands:
            batch_content += f"{cmd}\n"
        
        batch_content += "\necho Generating video clips...\n"
        for cmd in video_commands:
            batch_content += f"{cmd}\n"
        
        batch_content += "\necho Media generation completed!\n"
        batch_content += f"echo Check media/shots/ for screenshots and media/clips/ for video clips\n"
        
        # Shell script for Linux/Mac - executed from scripts/ dir
        shell_content = "#!/bin/bash\n"
        shell_content += "cd ..\n"  # Go up to lesson root  
        shell_content += f"echo \"Generating media for {lesson_name}...\"\n"
        shell_content += f"echo \"Creating {len(screenshot_commands)} screenshots and {len(video_commands)} video clips\"\n\n"
        
        shell_content += "echo \"Generating screenshots...\"\n"
        for cmd in screenshot_commands:
            shell_content += f"{cmd}\n"
        
        shell_content += "\necho \"Generating video clips...\"\n"  
        for cmd in video_commands:
            shell_content += f"{cmd}\n"
        
        shell_content += "\necho \"Media generation completed!\"\n"
        shell_content += "echo \"Check media/shots/ for screenshots and media/clips/ for video clips\"\n"
        
        # Create organized structure  
        analysis_path = base_path / "analysis"
        scripts_path = base_path / "scripts"
        analysis_path.mkdir(parents=True, exist_ok=True)
        scripts_path.mkdir(parents=True, exist_ok=True)
        
        # Write files to organized locations
        (analysis_path / "media.todo").write_text(media_todo_content, encoding="utf-8")
        (scripts_path / "generate_media.bat").write_text(batch_content, encoding="utf-8")
        (scripts_path / "generate_media.sh").write_text(shell_content, encoding="utf-8")
        
        return {
            "success": True,
            "screenshots_count": len(screenshot_commands),
            "video_clips_count": len(video_commands),
            "files_written": ["analysis/media.todo", "scripts/generate_media.bat", "scripts/generate_media.sh"],
            "golden_points_count": len(top_golden_points),
            "media_dir": str(media_path),
            "shots_dir": str(shots_path),
            "clips_dir": str(clips_path),
            "scripts_dir": str(scripts_path)
        }
        
    except Exception as e:
        return {"success": False, "error": f"Failed to generate shots todo: {str(e)}"}

def extract_code_references_with_context(analysis: ContentAnalysis, transcript_json_path: str, out_dir: str) -> Dict[str, Any]:
    """Advanced code extraction with context windows around code references"""
    try:
        base_path = Path(out_dir)
        code_path = base_path / "code"
        
        # Create platform-specific directories
        platform_dirs = {
            "tradestation": code_path / "tradestation",
            "tradingview": code_path / "tradingview", 
            "python": code_path / "python",
            "metatrader": code_path / "metatrader",
            "general": code_path / "misc",
        }
        for d in platform_dirs.values(): 
            d.mkdir(parents=True, exist_ok=True)
        
        # Load transcript for context extraction
        with open(transcript_json_path, 'r', encoding='utf-8') as f:
            transcript_data = json.load(f)
        
        segments = transcript_data.get('segments', [])
        if not segments:
            return {"success": False, "error": "No segments found in transcript"}
        
        # Enhanced code detection patterns
        code_patterns = {
            'tradestation': {
                'patterns': [
                    r'(?:tradestation|easylanguage|el\b)',
                    r'(?:input|variable|plot|condition)',
                    r'(?:buy|sell)\s+(?:market|limit|stop)',
                    r'(?:if|then|begin|end)\b'
                ],
                'extension': '.txt',
                'description': 'TradeStation EasyLanguage references'
            },
            'tradingview': {
                'patterns': [
                    r'(?:tradingview|pine\s*script|pinescript)',
                    r'(?:study|strategy|indicator)',
                    r'(?:plot|hline|bgcolor)',
                    r'(?:ta\.|math\.|str\.)'
                ],
                'extension': '.pine',
                'description': 'TradingView Pine Script references'
            },
            'python': {
                'patterns': [
                    r'(?:python|pandas|numpy|matplotlib)',
                    r'(?:import|def|class|if __name__)\b',
                    r'(?:pd\.|np\.|plt\.)'
                ],
                'extension': '.py',
                'description': 'Python trading code references'
            }
        }
        
        files_written = []
        platforms = set()
        
        # Always generate at least placeholder files if no code references
        if not analysis.code_references:
            placeholder_file = platform_dirs["general"] / "placeholder.md"
            placeholder_content = "# No Code References Found\n\n"
            placeholder_content += "No specific code references were detected in this lesson transcript.\n"
            placeholder_content += "This is common for theoretical or introductory lessons.\n\n"
            placeholder_content += "## Manual Review Needed\n"
            placeholder_content += "- [ ] Listen to audio for any code mentions\n"
            placeholder_content += "- [ ] Check for platform-specific terminology\n"
            placeholder_content += "- [ ] Add any implicit code concepts\n"
            
            placeholder_file.write_text(placeholder_content, encoding="utf-8")
            files_written.append(str(placeholder_file.relative_to(base_path)))
        else:
            # Generate stub files for each code reference
            for i, code_ref in enumerate(analysis.code_references, 1):
                platform = getattr(code_ref, 'platform', 'general').lower()
                platforms.add(platform)
                
                # Choose appropriate directory
                target_dir = platform_dirs.get(platform, platform_dirs["general"])
                
                # Create stub file
                stub_file = target_dir / f"ref-{i:02d}.md"
                
                # Get context from description and mentioned_at timestamp
                context = getattr(code_ref, 'description', 'Code reference detected')
                timestamp = getattr(code_ref, 'mentioned_at', 'unknown')
                
                stub_content = f"# Code Reference #{i:02d} - {platform.title()}\n\n"
                stub_content += f"**Timestamp:** {timestamp}\n"
                stub_content += f"**Platform:** {platform}\n"
                stub_content += f"**Context:** {context}\n\n"
                stub_content += "## Code Snippet\n"
                stub_content += "```\n"
                stub_content += "// TODO: Extract exact code from transcript\n"
                stub_content += "// Platform-specific implementation needed\n"
                stub_content += "```\n\n"
                stub_content += "## TODO (Code Agent)\n"
                stub_content += "- [ ] Extract exact snippet from transcript\n"
                stub_content += f"- [ ] Convert to {platform} syntax\n"
                stub_content += "- [ ] Add documentation and comments\n"
                stub_content += "- [ ] Create test/example usage\n"
                stub_content += "- [ ] Validate functionality\n\n"
                stub_content += "## Notes\n"
                stub_content += f"Generated from lesson transcript analysis.\n"
                stub_content += f"Manual review and code extraction required.\n"
                
                stub_file.write_text(stub_content, encoding="utf-8")
                files_written.append(str(stub_file.relative_to(base_path)))
        
        # Generate summary
        summary_content = f"# 💻 Code Extraction Summary\n\n"
        
        if analysis.code_references:
            summary_content += f"**Generated**: {len(analysis.code_references)} code stub files\n\n"
            summary_content += "## Detected Platforms\n\n"
            for platform in sorted(platforms):
                summary_content += f"- **{platform.title()}**\n"
            summary_content += f"\n## Stub Files Created\n\n"
            for file_path in files_written:
                summary_content += f"- `{file_path}`\n"
            summary_content += "\n## Next Steps\n\n"
            summary_content += "- [ ] Review each stub file\n"
            summary_content += "- [ ] Extract exact code snippets from transcript\n" 
            summary_content += "- [ ] Implement platform-specific syntax\n"
            summary_content += "- [ ] Test and validate functionality\n"
        else:
            summary_content += "No specific code references detected in this lesson.\n"
            summary_content += "Generated placeholder file for manual review.\n"
        
        (code_path / "extraction_summary.md").write_text(summary_content, encoding="utf-8")
        files_written.append("extraction_summary.md")
        
        return {
            "success": True,
            "code_files_generated": len(files_written) - 1,  # Exclude summary file
            "platforms_detected": sorted(list(platforms)) if platforms else ["general"],
            "files_written": files_written,
            "code_dir": str(code_path)
        }
        
    except Exception as e:
        return {"success": False, "error": f"Failed to extract code: {str(e)}"}

def _timestamp_to_seconds(timestamp: str) -> float:
    """Convert HH:MM:SS timestamp to seconds"""
    try:
        parts = timestamp.split(':')
        if len(parts) == 3:
            h, m, s = map(int, parts)
            return h * 3600 + m * 60 + s
        elif len(parts) == 2:  # mm:ss format
            m, s = map(int, parts)
            return m * 60 + s
        return 0.0
    except:
        return 0.0

def _ensure_hhmmss(ts: Optional[str]) -> str:
    """Ensure timestamp is in HH:MM:SS format"""
    if not ts:
        return "00:00:00"
    if re.match(r"^\d{2}:\d{2}:\d{2}$", ts):  # hh:mm:ss
        return ts
    m = re.match(r"^(\d{1,2}):(\d{2})$", ts)  # mm:ss
    if m:
        return f"00:{int(m.group(1)):02d}:{m.group(2)}"
    return "00:00:00"

def _pick_golden_points_for_shots(golden_points, max_n=10):
    """Select and sort golden points for media generation"""
    if not golden_points:
        return []
    
    def _key(gp):
        ts = _ensure_hhmmss(getattr(gp, "timestamp", None))
        imp = getattr(gp, "importance", 0) or 0
        return (-imp, ts)
    
    sorted_points = sorted(golden_points, key=_key)
    return sorted_points[:max_n]

# ──────────────────────────────────────────────────────────────────────────────
# Wrapper functions for direct JSON file usage

def generate_shots_todo_from_json(json_file: str, out_dir: str, video_path: str, lesson_name: str = "") -> Dict[str, Any]:
    """Generate FFmpeg commands for screenshots - wrapper that takes JSON file path"""
    try:
        # First analyze the transcript
        analysis_result = analyze_transcript_from_json(json_file, lesson_name)
        if not analysis_result.get("success"):
            return {"success": False, "error": f"Failed to analyze transcript: {analysis_result.get('error')}"}
        
        analysis_data = analysis_result["analysis"]
        
        # Convert dict data back to objects for compatibility
        golden_points = [
            GoldenPoint(
                text=gp["text"],
                timestamp=gp["timestamp"], 
                importance=gp["importance"],
                category=gp["category"]
            ) for gp in analysis_data.get("golden_points", [])
        ]
        trading_concepts = [
            TradingConcept(
                name=tc["name"],
                description=tc["description"],
                mentioned_at=tc["mentioned_at"],
                category=tc["category"],
                complexity=tc["complexity"]
            ) for tc in analysis_data.get("trading_concepts", [])
        ]
        code_references = [
            CodeReference(
                platform=cr["platform"],
                description=cr["description"],
                mentioned_at=cr["mentioned_at"],
                code_snippet=cr.get("snippet")
            ) for cr in analysis_data.get("code_references", [])
        ]
        
        analysis = ContentAnalysis(
            golden_points=golden_points,
            trading_concepts=trading_concepts, 
            code_references=code_references,
            educational_structure=analysis_data.get("educational_structure", {}),
            academic_research_triggers=analysis_data.get("academic_research_triggers", []),
            summary=analysis_data.get("summary", {})
        )
        
        # Generate shots todo
        return generate_shots_todo(analysis, video_path, out_dir, lesson_name)
        
    except Exception as e:
        return {"success": False, "error": f"Failed to generate shots todo from JSON: {str(e)}"}

def extract_code_references_from_json(json_file: str, out_dir: str, lesson_name: str = "") -> Dict[str, Any]:
    """Extract code references with context - wrapper that takes JSON file path"""
    try:
        # First analyze the transcript  
        analysis_result = analyze_transcript_from_json(json_file, lesson_name)
        if not analysis_result.get("success"):
            return {"success": False, "error": f"Failed to analyze transcript: {analysis_result.get('error')}"}
            
        analysis_data = analysis_result["analysis"]
        
        # Convert dict data back to objects for compatibility
        golden_points = [
            GoldenPoint(
                text=gp["text"],
                timestamp=gp["timestamp"], 
                importance=gp["importance"],
                category=gp["category"]
            ) for gp in analysis_data.get("golden_points", [])
        ]
        trading_concepts = [
            TradingConcept(
                name=tc["name"],
                description=tc["description"],
                mentioned_at=tc["mentioned_at"],
                category=tc["category"],
                complexity=tc["complexity"]
            ) for tc in analysis_data.get("trading_concepts", [])
        ]
        code_references = [
            CodeReference(
                platform=cr["platform"],
                description=cr["description"],
                mentioned_at=cr["mentioned_at"],
                code_snippet=cr.get("snippet")
            ) for cr in analysis_data.get("code_references", [])
        ]
        
        analysis = ContentAnalysis(
            golden_points=golden_points,
            trading_concepts=trading_concepts, 
            code_references=code_references,
            educational_structure=analysis_data.get("educational_structure", {}),
            academic_research_triggers=analysis_data.get("academic_research_triggers", []),
            summary=analysis_data.get("summary", {})
        )
        
        # Extract code references
        return extract_code_references_with_context(analysis, json_file, out_dir)
        
    except Exception as e:
        return {"success": False, "error": f"Failed to extract code from JSON: {str(e)}"}

# ──────────────────────────────────────────────────────────────────────────────
# Export Functions

def write_notes_md(analysis: ContentAnalysis, out_dir: str, lesson_name: str = "") -> Dict[str, Any]:
    """Write analysis results to organized markdown files"""
    try:
        base_path = Path(out_dir)
        analysis_path = base_path / "analysis"
        analysis_path.mkdir(parents=True, exist_ok=True)
        
        # Golden Points Markdown
        golden_points_md = "# 🌟 Golden Points\n\n"
        golden_points_md += f"*Generated from lesson: {lesson_name}*\n\n"
        
        for i, gp in enumerate(analysis.golden_points[:10], 1):  # Top 10
            icon = "⭐" * gp.importance
            golden_points_md += f"## {i}. {gp.category.title()} {icon}\n"
            golden_points_md += f"**Timestamp**: {gp.timestamp}\n\n"
            golden_points_md += f"{gp.text}\n\n"
            golden_points_md += "---\n\n"
        
        # Concepts Summary
        notes_md = f"# 📝 Lesson Notes: {lesson_name}\n\n"
        notes_md += f"## 📊 Summary\n\n"
        notes_md += f"- **Golden Points**: {len(analysis.golden_points)}\n"
        notes_md += f"- **Trading Concepts**: {len(analysis.trading_concepts)}\n"
        notes_md += f"- **Code References**: {len(analysis.code_references)}\n"
        notes_md += f"- **Academic Research Needed**: {len(analysis.academic_research_triggers)}\n\n"
        
        # Top Concepts by Category
        concepts_by_category = {}
        for concept in analysis.trading_concepts:
            if concept.category not in concepts_by_category:
                concepts_by_category[concept.category] = []
            concepts_by_category[concept.category].append(concept)
        
        for category, concepts in concepts_by_category.items():
            notes_md += f"### {category.replace('_', ' ').title()}\n"
            for concept in sorted(concepts, key=lambda x: len(x.mentioned_at), reverse=True)[:5]:
                times = ", ".join(concept.mentioned_at[:3])
                notes_md += f"- **{concept.name}** ({times})\n"
            notes_md += "\n"
        
        # Write files to analysis subdirectory
        (analysis_path / "golden-points.md").write_text(golden_points_md, encoding="utf-8")
        (analysis_path / "notes.md").write_text(notes_md, encoding="utf-8")
        
        return {
            "success": True,
            "files_written": ["analysis/golden-points.md", "analysis/notes.md"],
            "output_dir": str(analysis_path)
        }
        
    except Exception as e:
        return {"success": False, "error": f"Failed to write notes: {str(e)}"}

def write_concepts_json(analysis: ContentAnalysis, out_dir: str) -> Dict[str, Any]:
    """Write structured concepts data to organized JSON"""
    try:
        base_path = Path(out_dir)
        analysis_path = base_path / "analysis"
        analysis_path.mkdir(parents=True, exist_ok=True)
        
        concepts_data = {
            "trading_concepts": [
                {
                    "name": tc.name,
                    "category": tc.category,
                    "complexity": tc.complexity,
                    "mentioned_at": tc.mentioned_at,
                    "description": tc.description
                } for tc in analysis.trading_concepts
            ],
            "academic_triggers": analysis.academic_research_triggers,
            "summary": analysis.summary
        }
        
        (analysis_path / "concepts.json").write_text(
            json.dumps(concepts_data, ensure_ascii=False, indent=2), 
            encoding="utf-8"
        )
        
        return {
            "success": True,
            "file_written": "analysis/concepts.json",
            "concepts_count": len(analysis.trading_concepts)
        }
        
    except Exception as e:
        return {"success": False, "error": f"Failed to write concepts: {str(e)}"}

# ──────────────────────────────────────────────────────────────────────────────
# MCP Tools

def analyze_transcript_content(
    transcript_text: str,
    segments_data: List[Dict],
    lesson_info: Optional[Dict] = None
) -> ContentAnalysis:
    """Main analysis function - core logic without decorators"""
    
    # Extract different types of content
    golden_points = extract_golden_points(transcript_text, segments_data)
    trading_concepts = identify_trading_concepts(transcript_text, segments_data)
    code_references = find_code_references(transcript_text, segments_data)
    educational_structure = analyze_educational_structure(transcript_text, segments_data)
    academic_triggers = identify_academic_triggers(trading_concepts, golden_points, transcript_text)
    advanced_academic_needs = identify_advanced_academic_needs(trading_concepts, golden_points, transcript_text)
    
    # Generate enhanced summary
    lesson_name = lesson_info.get('name', '') if lesson_info else ''
    is_practice = any(word in lesson_name.lower() for word in ['practice', 'práctica'])
    
    summary = {
        'total_golden_points': len(golden_points),
        'high_importance_points': len([gp for gp in golden_points if gp.importance >= 4]),
        'total_concepts_identified': len(trading_concepts),
        'concepts_by_category': {
            cat: len([tc for tc in trading_concepts if tc.category == cat])
            for cat in set(tc.category for tc in trading_concepts)
        },
        'code_references_found': len(code_references),
        'academic_research_needed': len(academic_triggers),
        'advanced_research_topics': len(advanced_academic_needs),
        'high_priority_research': len([item for item in advanced_academic_needs if item['priority'] >= 4]),
        'lesson_type': 'practical' if is_practice else 'theoretical',
        'complexity_score': sum(c.complexity for c in trading_concepts) / len(trading_concepts) if trading_concepts else 0,
        'engagement_score': educational_structure.get('engagement_score', 0),
        'teaching_style': educational_structure.get('teaching_style', 'unknown'),
        'learning_effectiveness': educational_structure.get('learning_indicators', {})
    }
    
    return ContentAnalysis(
        golden_points=golden_points,
        trading_concepts=trading_concepts,
        code_references=code_references,
        educational_structure=educational_structure,
        academic_research_triggers=sorted(set(academic_triggers + [f"{item['topic']} ({item['category']})" for item in advanced_academic_needs])),
        summary=summary
    )

def analyze_transcript_from_json(json_file_path: str, lesson_name: str = "") -> Dict[str, Any]:
    """Analyze transcript from JSON file (main entry point)"""
    try:
        json_path = Path(json_file_path)
        if not json_path.exists():
            return {"success": False, "error": f"JSON file not found: {json_file_path}"}
        
        # Load transcript data
        with open(json_path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        
        transcript_text = data.get('text', '')
        segments = data.get('segments', [])
        
        if not transcript_text:
            return {"success": False, "error": "No transcript text found in JSON"}
        
        log.info(f"Analyzing transcript: {json_path.name}")
        log.info(f"Text length: {len(transcript_text)} characters")
        log.info(f"Segments: {len(segments)}")
        
        # Perform analysis
        analysis = analyze_transcript_content(
            transcript_text=transcript_text,
            segments_data=segments,
            lesson_info={'name': lesson_name}
        )
        
        # Convert to serializable format
        result = {
            "success": True,
            "lesson_name": lesson_name,
            "source_file": str(json_path),
            "analysis": {
                "golden_points": [
                    {
                        "text": gp.text,
                        "timestamp": gp.timestamp,
                        "importance": gp.importance,
                        "category": gp.category
                    } for gp in analysis.golden_points
                ],
                "trading_concepts": [
                    {
                        "name": tc.name,
                        "description": tc.description,
                        "mentioned_at": tc.mentioned_at,
                        "category": tc.category,
                        "complexity": tc.complexity
                    } for tc in analysis.trading_concepts
                ],
                "code_references": [
                    {
                        "platform": cr.platform,
                        "description": cr.description,
                        "mentioned_at": cr.mentioned_at,
                        "code_snippet": cr.code_snippet
                    } for cr in analysis.code_references
                ],
                "educational_structure": analysis.educational_structure,
                "academic_research_triggers": analysis.academic_research_triggers,
                "summary": analysis.summary
            }
        }
        
        log.info(f"Analysis completed: {result['analysis']['summary']}")
        return result
        
    except Exception as e:
        log.error(f"Analysis failed: {e}")
        return {"success": False, "error": f"Analysis failed: {str(e)}"}

def export_notes(json_file_path: str, output_dir: str, lesson_name: str = "") -> Dict[str, Any]:
    """Export analysis as markdown notes"""
    # First analyze
    analysis_result = analyze_transcript_from_json(json_file_path, lesson_name)
    if not analysis_result.get("success"):
        return analysis_result
    
    # Convert back to ContentAnalysis object for export
    analysis_data = analysis_result["analysis"]
    analysis = ContentAnalysis(
        golden_points=[GoldenPoint(**gp) for gp in analysis_data["golden_points"]],
        trading_concepts=[TradingConcept(**tc) for tc in analysis_data["trading_concepts"]],
        code_references=[CodeReference(**cr) for cr in analysis_data["code_references"]],
        educational_structure=analysis_data["educational_structure"],
        academic_research_triggers=analysis_data["academic_research_triggers"],
        summary=analysis_data["summary"]
    )
    
    return write_notes_md(analysis, output_dir, lesson_name)

def export_concepts(json_file_path: str, output_dir: str, lesson_name: str = "") -> Dict[str, Any]:
    """Export analysis as structured JSON"""
    # First analyze
    analysis_result = analyze_transcript_from_json(json_file_path, lesson_name)
    if not analysis_result.get("success"):
        return analysis_result
    
    # Convert back to ContentAnalysis object for export
    analysis_data = analysis_result["analysis"]
    analysis = ContentAnalysis(
        golden_points=[GoldenPoint(**gp) for gp in analysis_data["golden_points"]],
        trading_concepts=[TradingConcept(**tc) for tc in analysis_data["trading_concepts"]],
        code_references=[CodeReference(**cr) for cr in analysis_data["code_references"]],
        educational_structure=analysis_data["educational_structure"],
        academic_research_triggers=analysis_data["academic_research_triggers"],
        summary=analysis_data["summary"]
    )
    
    return write_concepts_json(analysis, output_dir)

def get_analysis_server_status() -> Dict[str, Any]:
    """Get content analysis server status"""
    return {
        "server": "Content Analysis Server",
        "status": "running",
        "version": "2.0",
        "capabilities": [
            "Golden points extraction with importance scoring",
            "Trading concepts identification with word boundaries", 
            "Code references detection",
            "Educational structure analysis",
            "Academic research triggers identification",
            "Markdown and JSON export"
        ],
        "knowledge_base_size": {
            "trading_indicators": len(TRADING_INDICATORS),
            "trading_strategies": len(TRADING_STRATEGIES), 
            "risk_management": len(RISK_MANAGEMENT),
            "platforms_tools": len(PLATFORMS_TOOLS),
            "academic_concepts": len(ACADEMIC_CONCEPTS)
        },
        "features": [
            "hh:mm:ss timestamp formatting",
            "Unicode normalization with accent removal",
            "Duplicate detection and filtering",
            "Enhanced categorization",
            "Export capabilities"
        ]
    }

# ──────────────────────────────────────────────────────────────────────────────
# Register functions as MCP tools (without overwriting function names)
mcp.tool()(analyze_transcript_from_json)
mcp.tool()(export_notes)
mcp.tool()(export_concepts)
mcp.tool()(generate_shots_todo_from_json)
mcp.tool()(extract_code_references_from_json)
mcp.tool()(get_analysis_server_status)

# ──────────────────────────────────────────────────────────────────────────────
def main():
    log.info("🚀 Starting Enhanced Content Analysis MCP Server")
    log.info("📋 Registered tools: analyze_transcript_from_json, export_notes, export_concepts, get_analysis_server_status")
    total_concepts = len(TRADING_INDICATORS | TRADING_STRATEGIES | RISK_MANAGEMENT | PLATFORMS_TOOLS | ACADEMIC_CONCEPTS)
    log.info(f"📚 Knowledge base loaded: {total_concepts} total concepts")
    mcp.run()

# CLI support
if __name__ == "__main__":
    import argparse
    
    ap = argparse.ArgumentParser(description="Content Analysis for Trading Course Transcripts")
    ap.add_argument("--json", help="Path to transcript JSON file")
    ap.add_argument("--lesson", default="", help="Lesson name/identifier")
    ap.add_argument("--out", help="Output directory for exports")
    ap.add_argument("--export", choices=["notes", "concepts", "both"], help="Export format")
    ap.add_argument("--server", action="store_true", help="Run MCP server")
    
    args = ap.parse_args()
    
    if args.server or not args.json:
        main()
    else:
        # CLI mode
        result = analyze_transcript_from_json(args.json, args.lesson)
        
        if result.get("success"):
            print(f"✅ Analysis completed for {args.lesson or 'transcript'}")
            summary = result["analysis"]["summary"]
            print(f"📊 Found: {summary['total_golden_points']} golden points, {summary['total_concepts_identified']} concepts")
            
            if args.out and args.export:
                if args.export in ["notes", "both"]:
                    export_result = export_notes(args.json, args.out, args.lesson)
                    if export_result.get("success"):
                        print(f"📝 Notes exported to {args.out}")
                
                if args.export in ["concepts", "both"]:
                    export_result = export_concepts(args.json, args.out, args.lesson)
                    if export_result.get("success"):
                        print(f"📊 Concepts exported to {args.out}")
        else:
            print(f"❌ Analysis failed: {result.get('error')}")