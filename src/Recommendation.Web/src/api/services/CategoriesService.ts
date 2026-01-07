/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PaginatedResultOfCategoryDto } from '../models/PaginatedResultOfCategoryDto';
import type { PaginatedResultOfProductDto } from '../models/PaginatedResultOfProductDto';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class CategoriesService {
    /**
     * @returns PaginatedResultOfCategoryDto OK
     * @throws ApiError
     */
    public static getCategoryHierarchy({
        page,
        pageSize,
    }: {
        page: number | string,
        pageSize: number | string,
    }): CancelablePromise<PaginatedResultOfCategoryDto> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/categories',
            query: {
                'page': page,
                'pageSize': pageSize,
            },
        });
    }
    /**
     * @returns PaginatedResultOfCategoryDto OK
     * @throws ApiError
     */
    public static getCategoryById({
        id,
    }: {
        id: number,
    }): CancelablePromise<PaginatedResultOfCategoryDto> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/categories/{id}',
            path: {
                'id': id,
            },
        });
    }
    /**
     * @returns PaginatedResultOfProductDto OK
     * @throws ApiError
     */
    public static getProductsByCategory({
        id,
        page,
        pageSize,
    }: {
        id: number,
        page: number | string,
        pageSize: number | string,
    }): CancelablePromise<PaginatedResultOfProductDto> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/categories/{id}/products',
            path: {
                'id': id,
            },
            query: {
                'page': page,
                'pageSize': pageSize,
            },
        });
    }
}
