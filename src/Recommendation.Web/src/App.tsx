import { useState, useCallback } from 'react';
import type { ProductDto } from './services/api';
import { HomePage } from './pages/HomePage';
import { ProductPage } from './pages/ProductPage';
import { SearchBox } from './components/SearchBox';
import { useRecentProducts } from './hooks/useRecentProducts';
import './App.css';

type View = 
  | { type: 'home'; selectedCategoryId?: number; selectedCategoryName?: string }
  | { type: 'product'; productId: number };

function App() {
  const [view, setView] = useState<View>({ type: 'home' });
  const [history, setHistory] = useState<View[]>([]);
  const { recentProducts, addRecentProduct } = useRecentProducts();

  const navigate = useCallback((newView: View) => {
    setHistory(prev => [...prev, view]);
    setView(newView);
  }, [view]);

  const handleProductClick = (product: ProductDto) => {
    addRecentProduct(product);
    navigate({ type: 'product', productId: Number(product.productId) });
  };

  const handleBack = useCallback(() => {
    if (history.length > 0) {
      const previousView = history[history.length - 1];
      setHistory(prev => prev.slice(0, -1));
      setView(previousView);
    } else {
      setView({ type: 'home' });
    }
  }, [history]);

  const handleHome = () => {
    setHistory([]);
    setView({ type: 'home' });
  };

  const handleSearchProductSelect = (productId: number) => {
    navigate({ type: 'product', productId });
  };

  const handleSearchCategorySelect = (categoryId: number, categoryName: string) => {
    navigate({ type: 'home', selectedCategoryId: categoryId, selectedCategoryName: categoryName });
  };

  const handleSearchBrandSelect = (_brandId: number) => {
    navigate({ type: 'home' });
  };

  const canGoBack = history.length > 0 || view.type !== 'home' || view.selectedCategoryId !== undefined;

  return (
    <div className="app">
      <header className="app__header">
        <div className="app__header-content">
          <button className="app__logo" onClick={handleHome}>
            <span className="app__logo-icon">🔗</span>
            <span className="app__logo-text">Graph Recommendations</span>
          </button>
          <SearchBox
            onProductSelect={handleSearchProductSelect}
            onCategorySelect={handleSearchCategorySelect}
            onBrandSelect={handleSearchBrandSelect}
          />
        </div>
      </header>
      <div className="app__content">
        {view.type === 'home' ? (
          <HomePage 
            onProductClick={handleProductClick}
            onCategorySelect={handleSearchCategorySelect}
            onBack={canGoBack ? handleBack : undefined}
            selectedCategoryId={view.selectedCategoryId}
            selectedCategoryName={view.selectedCategoryName}
            recentProducts={recentProducts}
          />
        ) : (
          <ProductPage
            productId={view.productId}
            onProductClick={handleProductClick}
            onBack={handleBack}
          />
        )}
      </div>
    </div>
  );
}

export default App;
