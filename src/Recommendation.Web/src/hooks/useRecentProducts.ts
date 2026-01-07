import { useState, useEffect, useCallback } from 'react';
import type { ProductDto } from '../services/api';

const STORAGE_KEY = 'recentProducts';
const MAX_RECENT = 6;

export interface RecentProduct {
  productId: number;
  productName: string;
  brandName?: string;
  categoryId?: number;
  brandId?: number;
  viewedAt: number;
}

export function useRecentProducts() {
  const [recentProducts, setRecentProducts] = useState<RecentProduct[]>([]);

  useEffect(() => {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored) {
      try {
        setRecentProducts(JSON.parse(stored));
      } catch {
        localStorage.removeItem(STORAGE_KEY);
      }
    }
  }, []);

  const addRecentProduct = useCallback((product: ProductDto) => {
    setRecentProducts(prev => {
      const filtered = prev.filter(p => p.productId !== product.productId);
      const newProduct: RecentProduct = {
        productId: Number(product.productId),
        productName: product.productName,
        brandName: product.brandName ?? undefined,
        categoryId: product.categoryId != null ? Number(product.categoryId) : undefined,
        brandId: product.brandId != null ? Number(product.brandId) : undefined,
        viewedAt: Date.now(),
      };
      const updated: RecentProduct[] = [newProduct, ...filtered].slice(0, MAX_RECENT);
      
      localStorage.setItem(STORAGE_KEY, JSON.stringify(updated));
      return updated;
    });
  }, []);

  return { recentProducts, addRecentProduct };
}

